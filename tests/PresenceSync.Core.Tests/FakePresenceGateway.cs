using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Tests;

/// <summary>
/// In-memory Teams: keeps the "real" presence (Teams clients), the app session and the preferred presence
/// per tenant and aggregates them like Graph does (preferred > sessions; DnD > Busy > Available > Away).
/// </summary>
internal sealed class FakePresenceGateway : IPresenceGateway
{
    private readonly object _sync = new();

    public Dictionary<string, PresenceInfo> Real { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, (SyncKind Kind, TimeSpan Expiration)> Sessions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, (ManualStatus Status, TimeSpan? Expiration)> Preferred { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Operations that fail: key = "tenant:operation" or "tenant:*".</summary>
    public Dictionary<string, TenantAccessException> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Graph lag: when true, cleared sessions still show up in reads (until set false).</summary>
    public bool StaleReadsAfterClear { get; set; }

    private readonly Dictionary<string, PresenceInfo> _stale = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void SetReal(string tenant, string availability, string activity) => Real[tenant] = new(availability, activity);

    public int CountCalls(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public Task<PresenceInfo> GetPresenceAsync(string tenantId, CancellationToken ct)
    {
        Record(tenantId, "get");
        lock (_sync)
        {
            if (StaleReadsAfterClear && _stale.TryGetValue(tenantId, out var stale))
                return Task.FromResult(stale);
            return Task.FromResult(Aggregate(tenantId));
        }
    }

    public Task SetSessionAsync(string tenantId, SyncKind kind, TimeSpan expiration, CancellationToken ct)
    {
        Record(tenantId, "set", kind.ToString());
        lock (_sync) Sessions[tenantId] = (kind, expiration);
        return Task.CompletedTask;
    }

    public Task ClearSessionAsync(string tenantId, CancellationToken ct)
    {
        Record(tenantId, "clear");
        lock (_sync)
        {
            if (StaleReadsAfterClear)
                _stale[tenantId] = Aggregate(tenantId);
            Sessions.Remove(tenantId);
        }
        return Task.CompletedTask;
    }

    public Task SetPreferredAsync(string tenantId, ManualStatus status, TimeSpan? expiration, CancellationToken ct)
    {
        Record(tenantId, "pref", status.ToString());
        lock (_sync) Preferred[tenantId] = (status, expiration);
        return Task.CompletedTask;
    }

    public Task ClearPreferredAsync(string tenantId, CancellationToken ct)
    {
        Record(tenantId, "clearpref");
        lock (_sync) Preferred.Remove(tenantId);
        return Task.CompletedTask;
    }

    private void Record(string tenantId, string operation, string? detail = null)
    {
        lock (_sync)
        {
            Calls.Add(detail is null ? $"{operation}:{tenantId}" : $"{operation}:{tenantId}:{detail}");
            if (Failures.TryGetValue($"{tenantId}:{operation}", out var ex) || Failures.TryGetValue($"{tenantId}:*", out ex))
                throw ex;
        }
    }

    private PresenceInfo Aggregate(string tenantId)
    {
        if (Preferred.TryGetValue(tenantId, out var pref))
            return pref.Status.ToPreferredPresence();

        var real = Real.GetValueOrDefault(tenantId, new PresenceInfo("Available", "Available"));
        if (Sessions.TryGetValue(tenantId, out var session))
        {
            var sessionPresence = PresenceRules.SessionPresence(session.Kind);
            if (Rank(sessionPresence) > Rank(real))
                return sessionPresence;
        }
        return real;
    }

    private static int Rank(PresenceInfo p) => p.Availability switch
    {
        "DoNotDisturb" => 3,
        "Busy" => 2,
        "Available" => 1,
        _ => 0,
    };
}
