using PresenceSync.Core.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PresenceSync.Core.Configuration;
using PresenceSync.Core.Presence;

namespace PresenceSync.Core.Sync;

public sealed record SyncOptions(
    bool AutoSync,
    TimeSpan SessionExpiration,
    TimeSpan RefreshThreshold,
    TimeSpan SourceCooldown)
{
    public static SyncOptions Default { get; } = new(true, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(60));

    public static SyncOptions FromConfig(AppConfig config) => new(
        config.AutoSync,
        PresenceRules.ClampSessionExpiration(TimeSpan.FromMinutes(config.SessionExpirationMinutes)),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromSeconds(Math.Clamp(config.SourceCooldownSeconds, 0, 600)));
}

/// <summary>Result per tenant: <see cref="Error"/> in the UI language, <see cref="LogError"/> in English.</summary>
public sealed record TenantResult(string TenantId, string Name, bool Success, string? Error, string? LogError = null);

/// <summary>
/// Stateful sync state machine: polls presence of all tenants, applies <see cref="SyncPlanner"/> decisions,
/// tracks own sessions, manual status, backoff and auth state per tenant. All operations are serialized.
/// A failure in one tenant never blocks the others.
/// </summary>
public sealed class SyncEngine
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private readonly IPresenceGateway _gateway;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<TenantState> _tenants = [];
    private SyncOptions _options;

    public SyncEngine(IPresenceGateway gateway, SyncOptions options, TimeProvider time, ILogger<SyncEngine>? log = null)
    {
        _gateway = gateway;
        _options = options;
        _time = time;
        _log = log ?? NullLogger<SyncEngine>.Instance;
        Snapshot = new SyncSnapshot(time.GetUtcNow(), options.AutoSync, []);
    }

    public event EventHandler<SyncSnapshot>? SnapshotChanged;

    public SyncSnapshot Snapshot { get; private set; }

    public SyncOptions Options => _options;

    /// <summary>Applies a new tenant list and options. Sessions in removed or disabled tenants are cleared.</summary>
    public async Task ConfigureAsync(IEnumerable<TenantDescriptor> tenants, SyncOptions options, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _options = options;
            var wanted = tenants.ToList();
            var dropped = _tenants
                .Where(t => t.OwnSession is not null
                            && !wanted.Any(w => w.Enabled && Same(w.TenantId, t.TenantId)))
                .ToList();
            await Task.WhenAll(dropped.Select(t => ClearAsync(t, ct)));

            var next = new List<TenantState>();
            foreach (var w in wanted)
            {
                var state = _tenants.FirstOrDefault(t => Same(t.TenantId, w.TenantId))
                            ?? new TenantState(w.TenantId, w.Name, w.Enabled);
                state.Name = w.Name;
                if (state.Enabled != w.Enabled)
                {
                    state.Enabled = w.Enabled;
                    ResetHealth(state);
                }
                next.Add(state);
            }
            _tenants.Clear();
            _tenants.AddRange(next);
        }
        finally
        {
            ReleaseAndPublish();
        }
    }

    public async Task SetAutoSyncAsync(bool enabled, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _options = _options with { AutoSync = enabled };
        }
        finally
        {
            ReleaseAndPublish();
        }
        _log.LogInformation("Auto-sync {State}", enabled ? "on" : "off");
    }

    /// <summary>Called after a successful interactive sign-in: the tenant takes part again.</summary>
    public async Task MarkSignedInAsync(string tenantId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (Find(tenantId) is { } t)
                ResetHealth(t);
        }
        finally
        {
            ReleaseAndPublish();
        }
    }

    /// <summary>One sync cycle (spec 8.2).</summary>
    public async Task<SyncSnapshot> PollAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            ExpireLocalState(now);

            var callable = _tenants.Where(t => t.CanCall(now)).ToList();
            var reads = await Task.WhenAll(callable.Select(async t =>
            {
                var ok = await TryAsync(t, "read presence", async () =>
                {
                    t.Presence = await _gateway.GetPresenceAsync(t.TenantId, ct);
                    t.PresenceReadAt = _time.GetUtcNow();
                }, ct);
                return (Tenant: t, Ok: ok);
            }));

            var views = reads
                .Where(r => r.Ok)
                .Select(r => new TenantView(r.Tenant.TenantId, r.Tenant.Presence!, r.Tenant.OwnSession,
                    r.Tenant.Manual is not null, r.Tenant.Suppression))
                .ToList();

            var plan = SyncPlanner.Plan(views, _options.AutoSync, now, _options.RefreshThreshold);

            foreach (var t in _tenants)
            {
                var wasSource = t.IsSource;
                t.IsSource = plan.Sources.Contains(t.TenantId);
                if (t.IsSource != wasSource)
                    _log.LogInformation("{Tenant}: {Change} ({Presence})", t.Name,
                        t.IsSource ? "became a source" : "is no longer a source", t.Presence);
            }

            await Task.WhenAll(plan.Actions.Select(a => ExecuteAsync(a, ct)));
        }
        finally
        {
            ReleaseAndPublish();
        }
        return Snapshot;
    }

    /// <summary>Sets a manual status (setUserPreferredPresence). <paramref name="tenantIds"/> null = all enabled tenants.</summary>
    public async Task<IReadOnlyList<TenantResult>> SetManualAsync(
        IReadOnlyCollection<string>? tenantIds, ManualStatus status, TimeSpan? duration, CancellationToken ct)
    {
        IReadOnlyList<TenantResult> results;
        await _lock.WaitAsync(ct);
        try
        {
            results = await Task.WhenAll(Select(tenantIds).Select(async t =>
            {
                var now = _time.GetUtcNow();
                var ok = await TryAsync(t, $"set status {status}",
                    () => _gateway.SetPreferredAsync(t.TenantId, status, duration, ct), ct);
                if (ok)
                {
                    t.Manual = new ManualState(status, now + (duration ?? status.GraphDefaultExpiration()));
                    t.Suppression = null;
                    // The manual status overrides the session anyway; dropping it avoids a stale Busy afterwards.
                    if (t.OwnSession is not null)
                        await ClearAsync(t, ct);
                }
                return Result(t, ok);
            }));
        }
        finally
        {
            ReleaseAndPublish();
        }
        _log.LogInformation("Manual status {Status} ({Duration}) set: {Result}", status,
            duration?.ToString() ?? "default", Describe(results));
        return results;
    }

    /// <summary>Removes the manual status (clearUserPreferredPresence). <paramref name="tenantIds"/> null = all.</summary>
    public async Task<IReadOnlyList<TenantResult>> ResetManualAsync(IReadOnlyCollection<string>? tenantIds, CancellationToken ct)
    {
        IReadOnlyList<TenantResult> results;
        await _lock.WaitAsync(ct);
        try
        {
            results = await Task.WhenAll(Select(tenantIds).Select(async t =>
            {
                var ok = await TryAsync(t, "reset status", () => _gateway.ClearPreferredAsync(t.TenantId, ct), ct);
                if (ok)
                {
                    // Graph may still report the old preferred state for a moment; don't mirror it.
                    // If the status was set outside the app, DnD is the case that matters.
                    var previous = (t.Manual?.Status ?? ManualStatus.DoNotDisturb).ToPreferredPresence();
                    t.Suppression = new SourceSuppression(_time.GetUtcNow() + _options.SourceCooldown, previous);
                    t.Manual = null;
                }
                return Result(t, ok);
            }));
        }
        finally
        {
            ReleaseAndPublish();
        }
        _log.LogInformation("Manual status reset: {Result}", Describe(results));
        return results;
    }

    /// <summary>Best-effort cleanup on exit/logoff: clears every own session. Never throws.</summary>
    public async Task ClearOwnSessionsAsync(CancellationToken ct)
    {
        try
        {
            await _lock.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            var open = _tenants.Where(t => t.OwnSession is not null && !t.IsBlocked).ToList();
            if (open.Count > 0)
                _log.LogInformation("Ending {Count} own session(s)", open.Count);
            await Task.WhenAll(open.Select(t => ClearAsync(t, ct)));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cleanup of own sessions incomplete");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ExecuteAsync(SyncAction action, CancellationToken ct)
    {
        var t = Find(action.TenantId)!;
        switch (action)
        {
            case SetSessionAction set:
                var started = _time.GetUtcNow();
                var expiration = _options.SessionExpiration;
                if (await TryAsync(t, "set session", () => _gateway.SetSessionAsync(t.TenantId, set.Kind, expiration, ct), ct))
                {
                    var isNew = t.OwnSession is null || t.OwnSession.Kind != set.Kind;
                    t.OwnSession = new OwnSession(set.Kind, isNew ? started : t.OwnSession!.Since, started + expiration);
                    if (isNew)
                        _log.LogInformation("{Tenant}: sync session {Kind} set", t.Name, set.Kind);
                }
                break;

            case ClearSessionAction:
                if (await ClearAsync(t, ct))
                    _log.LogInformation("{Tenant}: sync session ended", t.Name);
                break;
        }
    }

    private async Task<bool> ClearAsync(TenantState t, CancellationToken ct)
    {
        var kind = t.OwnSession?.Kind ?? SyncKind.Busy;
        var ok = await TryAsync(t, "end session", () => _gateway.ClearSessionAsync(t.TenantId, ct), ct);
        if (ok)
        {
            t.OwnSession = null;
            t.Suppression = new SourceSuppression(_time.GetUtcNow() + _options.SourceCooldown, PresenceRules.SessionPresence(kind));
        }
        return ok;
    }

    private void ExpireLocalState(DateTimeOffset now)
    {
        foreach (var t in _tenants)
        {
            if (t.OwnSession is { } s && s.Until <= now)
            {
                _log.LogInformation("{Tenant}: sync session expired", t.Name);
                t.OwnSession = null;
                t.Suppression = new SourceSuppression(now + _options.SourceCooldown, PresenceRules.SessionPresence(s.Kind));
            }
            if (t.Manual is { } m && m.Until <= now)
                t.Manual = null;
            if (t.Suppression is { } sup && sup.Until <= now)
                t.Suppression = null;
        }
    }

    private async Task<bool> TryAsync(TenantState t, string operation, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
            if (t.Health != TenantHealth.Ok)
                _log.LogInformation("{Tenant}: connected", t.Name);
            t.Health = TenantHealth.Ok;
            t.HealthMessage = null;
            t.HealthLog = null;
            t.ConsecutiveFailures = 0;
            t.BackoffUntil = null;
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TenantAccessException ex)
        {
            Fail(t, operation, ex);
        }
        catch (Exception ex)
        {
            Fail(t, operation, new TenantAccessException(TenantFailureKind.Transient,
                new LocalizedMessage(nameof(Strings.Error_Unexpected), ex.GetType().Name), inner: ex));
        }
        return false;
    }

    private void Fail(TenantState t, string operation, TenantAccessException ex)
    {
        var now = _time.GetUtcNow();
        t.HealthMessage = ex.UserMessage;
        t.HealthLog = ex.Message;
        switch (ex.Kind)
        {
            case TenantFailureKind.AuthRequired:
                t.Health = TenantHealth.AuthRequired;
                _log.LogWarning("{Tenant}: {Operation} – sign-in required: {Message}", t.Name, operation, ex.Message);
                break;
            case TenantFailureKind.Disabled:
                t.Health = TenantHealth.Disabled;
                _log.LogError("{Tenant}: {Operation} – tenant blocked: {Message}", t.Name, operation, ex.Message);
                break;
            default:
                t.ConsecutiveFailures++;
                var delay = Backoff(t.ConsecutiveFailures);
                if (ex.RetryAfter is { } retryAfter && retryAfter > delay)
                    delay = retryAfter;
                t.BackoffUntil = now + delay;
                t.Health = TenantHealth.Degraded;
                _log.LogWarning(ex.InnerException, "{Tenant}: {Operation} failed ({Kind}, attempt {Attempt}), next attempt in {Delay}: {Message}",
                    t.Name, operation, ex.Kind, t.ConsecutiveFailures, delay, ex.UserMessage);
                break;
        }
    }

    internal static TimeSpan Backoff(int failures)
    {
        var seconds = 5 * Math.Pow(2, Math.Min(failures, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
    }

    private static void ResetHealth(TenantState t)
    {
        t.Health = TenantHealth.Unknown;
        t.HealthMessage = null;
            t.HealthLog = null;
        t.ConsecutiveFailures = 0;
        t.BackoffUntil = null;
    }

    private IEnumerable<TenantState> Select(IReadOnlyCollection<string>? tenantIds) =>
        _tenants.Where(t => t.Enabled && !t.IsBlocked
                            && (tenantIds is null || tenantIds.Any(id => Same(id, t.TenantId))));

    private TenantState? Find(string tenantId) => _tenants.FirstOrDefault(t => Same(t.TenantId, tenantId));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static TenantResult Result(TenantState t, bool ok) =>
        new(t.TenantId, t.Name, ok, ok ? null : t.HealthMessage, ok ? null : t.HealthLog);

    private static string Describe(IReadOnlyCollection<TenantResult> results) =>
        results.Count == 0
            ? "no active, signed-in tenant – nothing set"
            : string.Join(", ", results.Select(r => r.Success ? $"{r.Name} ok" : $"{r.Name} failed: {r.LogError ?? r.Error}"));

    /// <summary>Builds the snapshot while still holding the lock, then raises the event outside of it.</summary>
    private void ReleaseAndPublish()
    {
        var snapshot = new SyncSnapshot(_time.GetUtcNow(), _options.AutoSync, _tenants.Select(t => new TenantSnapshot(
            t.TenantId, t.Name, t.Enabled, t.Presence,
            t.Manual is not null ? TenantRole.Manual
            : t.OwnSession is not null ? TenantRole.Synced
            : t.IsSource ? TenantRole.Source
            : TenantRole.None,
            t.Health, t.HealthMessage, t.Manual, t.OwnSession, t.BackoffUntil)).ToList());
        Snapshot = snapshot;
        _lock.Release();

        try
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in SnapshotChanged handler");
        }
    }
}
