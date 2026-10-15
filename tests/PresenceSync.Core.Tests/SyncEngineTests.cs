using Microsoft.Extensions.Time.Testing;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Tests;

public sealed class SyncEngineTests
{
    private const string A = "tenant-a";
    private const string B = "tenant-b";
    private const string C = "tenant-c";

    private readonly FakePresenceGateway _gateway = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly SyncEngine _engine;

    public SyncEngineTests()
    {
        _engine = new SyncEngine(_gateway, SyncOptions.Default, _time);
        _engine.ConfigureAsync(
            [new(A, "Kunde A", true), new(B, "Kunde B", true), new(C, "LEVEL21", true)],
            SyncOptions.Default, CancellationToken.None).GetAwaiter().GetResult();
    }

    private Task<SyncSnapshot> Poll() => _engine.PollAsync(CancellationToken.None);

    private TenantSnapshot Tenant(SyncSnapshot s, string id) => s.Tenants.Single(t => t.TenantId == id);

    [Fact]
    public async Task All_available_does_nothing()
    {
        var snapshot = await Poll();

        Assert.DoesNotContain(_gateway.Calls, c => !c.StartsWith("get"));
        Assert.Equal(TrayState.Available, snapshot.TrayState);
    }

    [Fact]
    public async Task Call_in_one_tenant_sets_busy_in_all_others()
    {
        _gateway.SetReal(A, "Busy", "InACall");

        var snapshot = await Poll();

        Assert.Contains("set:tenant-b:Busy", _gateway.Calls);
        Assert.Contains("set:tenant-c:Busy", _gateway.Calls);
        Assert.DoesNotContain(_gateway.Calls, c => c.StartsWith("set:tenant-a"));
        Assert.Equal(TimeSpan.FromMinutes(5), _gateway.Sessions[B].Expiration);
        Assert.Equal(TenantRole.Source, Tenant(snapshot, A).Role);
        Assert.Equal(TenantRole.Synced, Tenant(snapshot, B).Role);
        Assert.Equal(TrayState.Busy, snapshot.TrayState);
    }

    [Theory]
    [InlineData("DoNotDisturb", "DoNotDisturb")]
    [InlineData("DoNotDisturb", "Presenting")]
    public async Task Dnd_or_presenting_source_is_mirrored_as_dnd(string availability, string activity)
    {
        _gateway.SetReal(A, availability, activity);

        await Poll();

        Assert.Equal(SyncKind.DoNotDisturb, _gateway.Sessions[B].Kind);
        Assert.Equal(SyncKind.DoNotDisturb, _gateway.Sessions[C].Kind);
    }

    [Fact]
    public async Task Meeting_from_calendar_counts_as_source()
    {
        _gateway.SetReal(B, "Busy", "InAMeeting");

        await Poll();

        Assert.Equal(SyncKind.Busy, _gateway.Sessions[A].Kind);
        Assert.Equal(SyncKind.Busy, _gateway.Sessions[C].Kind);
    }

    [Fact]
    public async Task Plain_busy_is_not_a_source()
    {
        _gateway.SetReal(A, "Busy", "Busy");

        await Poll();

        Assert.Empty(_gateway.Sessions);
    }

    [Fact]
    public async Task Synced_tenant_is_never_treated_as_source()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        // B and C now read Busy/InACall because of the app's own session.
        _time.Advance(TimeSpan.FromSeconds(20));
        var snapshot = await Poll();

        Assert.Equal(TenantRole.Synced, Tenant(snapshot, B).Role);
        Assert.DoesNotContain(_gateway.Calls, c => c.StartsWith("set:tenant-a"));
    }

    [Fact]
    public async Task Call_end_clears_sessions_immediately()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        _gateway.SetReal(A, "Available", "Available");
        _time.Advance(TimeSpan.FromSeconds(20));
        var snapshot = await Poll();

        Assert.Contains("clear:tenant-b", _gateway.Calls);
        Assert.Contains("clear:tenant-c", _gateway.Calls);
        Assert.Empty(_gateway.Sessions);
        Assert.All(snapshot.Tenants, t => Assert.Equal(TenantRole.None, t.Role));
    }

    [Fact]
    public async Task Stale_read_after_clear_does_not_ping_pong()
    {
        _gateway.StaleReadsAfterClear = true;
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        _gateway.SetReal(A, "Available", "Available");
        _time.Advance(TimeSpan.FromSeconds(20));
        await Poll(); // clears B and C; Graph still reports Busy/InACall there for a while

        _time.Advance(TimeSpan.FromSeconds(20));
        await Poll();

        Assert.Equal(0, _gateway.CountCalls("set:tenant-a"));
        Assert.Empty(_gateway.Sessions);
    }

    [Fact]
    public async Task Real_call_after_cooldown_is_detected_again()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();
        _gateway.SetReal(A, "Available", "Available");
        _time.Advance(TimeSpan.FromSeconds(20));
        await Poll();

        _gateway.SetReal(B, "Busy", "InACall");
        _time.Advance(TimeSpan.FromSeconds(61));
        await Poll();

        Assert.Contains("set:tenant-a:Busy", _gateway.Calls);
    }

    [Fact]
    public async Task Session_is_refreshed_only_when_less_than_two_minutes_remain()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();
        Assert.Equal(1, _gateway.CountCalls("set:tenant-b"));

        for (var i = 0; i < 8; i++) // 2:40 min
        {
            _time.Advance(TimeSpan.FromSeconds(20));
            await Poll();
        }
        Assert.Equal(1, _gateway.CountCalls("set:tenant-b"));

        _time.Advance(TimeSpan.FromSeconds(30)); // 3:10 min → remaining 1:50
        await Poll();
        Assert.Equal(2, _gateway.CountCalls("set:tenant-b"));
    }

    [Fact]
    public async Task Kind_change_from_busy_to_dnd_updates_session()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        _gateway.SetReal(A, "DoNotDisturb", "Presenting");
        _time.Advance(TimeSpan.FromSeconds(20));
        await Poll();

        Assert.Equal(SyncKind.DoNotDisturb, _gateway.Sessions[B].Kind);
    }

    [Fact]
    public async Task Manual_status_has_priority_over_auto_sync()
    {
        await _engine.SetManualAsync([B], ManualStatus.Available, TimeSpan.FromHours(1), CancellationToken.None);
        _gateway.SetReal(A, "Busy", "InACall");

        var snapshot = await Poll();

        Assert.False(_gateway.Sessions.ContainsKey(B));
        Assert.True(_gateway.Sessions.ContainsKey(C));
        Assert.Equal(TenantRole.Manual, Tenant(snapshot, B).Role);
    }

    [Fact]
    public async Task Setting_manual_status_clears_existing_session_in_that_tenant()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        await _engine.SetManualAsync([B], ManualStatus.Away, null, CancellationToken.None);

        Assert.False(_gateway.Sessions.ContainsKey(B));
        Assert.Equal(TimeSpan.FromDays(7), _engine.Snapshot.Tenants.Single(t => t.TenantId == B).Manual!.Until - _time.GetUtcNow());
    }

    [Fact]
    public async Task Manual_status_expires_locally()
    {
        await _engine.SetManualAsync([B], ManualStatus.Busy, TimeSpan.FromMinutes(30), CancellationToken.None);
        _gateway.Preferred.Remove(B); // Graph expired it as well
        _gateway.SetReal(A, "Busy", "InACall");

        _time.Advance(TimeSpan.FromMinutes(31));
        await Poll();

        Assert.True(_gateway.Sessions.ContainsKey(B));
    }

    [Fact]
    public async Task Manual_dnd_in_all_tenants_creates_no_sessions()
    {
        await _engine.SetManualAsync(null, ManualStatus.DoNotDisturb, TimeSpan.FromHours(1), CancellationToken.None);

        await Poll();

        Assert.Empty(_gateway.Sessions);
        Assert.Equal(3, _gateway.CountCalls("pref:"));
    }

    [Fact]
    public async Task Reset_of_manual_dnd_does_not_mirror_stale_dnd()
    {
        await _engine.SetManualAsync([A], ManualStatus.DoNotDisturb, null, CancellationToken.None);
        await _engine.ResetManualAsync([A], CancellationToken.None);
        _gateway.SetReal(A, "DoNotDisturb", "DoNotDisturb"); // Graph lag

        await Poll();

        Assert.Empty(_gateway.Sessions);
    }

    [Fact]
    public async Task Failing_tenant_does_not_block_others_and_backs_off()
    {
        _gateway.Failures[$"{B}:*"] = new TenantAccessException(TenantFailureKind.Throttled, "Throttling", TimeSpan.FromSeconds(90));
        _gateway.SetReal(A, "Busy", "InACall");

        var snapshot = await Poll();

        Assert.True(_gateway.Sessions.ContainsKey(C));
        var b = Tenant(snapshot, B);
        Assert.Equal(TenantHealth.Degraded, b.Health);
        Assert.Equal(_time.GetUtcNow() + TimeSpan.FromSeconds(90), b.BackoffUntil);

        _gateway.Failures.Clear();
        _time.Advance(TimeSpan.FromSeconds(20));
        await Poll();
        Assert.Equal(1, _gateway.CountCalls("get:tenant-b")); // still in backoff

        _time.Advance(TimeSpan.FromSeconds(80));
        snapshot = await Poll();
        Assert.Equal(TenantHealth.Ok, Tenant(snapshot, B).Health);
        Assert.True(_gateway.Sessions.ContainsKey(B));
    }

    [Fact]
    public void Backoff_grows_exponentially_and_is_capped()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), SyncEngine.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(20), SyncEngine.Backoff(2));
        Assert.Equal(TimeSpan.FromMinutes(5), SyncEngine.Backoff(20));
    }

    [Fact]
    public async Task Auth_required_marks_tenant_and_skips_it_until_signed_in()
    {
        _gateway.Failures[$"{B}:get"] = new TenantAccessException(TenantFailureKind.AuthRequired, "MFA erforderlich");
        _gateway.SetReal(A, "Busy", "InACall");

        var snapshot = await Poll();
        Assert.Equal(TenantHealth.AuthRequired, Tenant(snapshot, B).Health);
        Assert.Equal(TrayState.Warning, snapshot.TrayState);
        Assert.True(_gateway.Sessions.ContainsKey(C));

        _time.Advance(TimeSpan.FromMinutes(10));
        await Poll();
        Assert.Equal(1, _gateway.CountCalls("get:tenant-b"));

        _gateway.Failures.Clear();
        await _engine.MarkSignedInAsync(B, CancellationToken.None);
        snapshot = await Poll();
        Assert.Equal(TenantHealth.Ok, Tenant(snapshot, B).Health);
    }

    [Fact]
    public async Task Disabled_tenant_keeps_others_running()
    {
        _gateway.Failures[$"{C}:*"] = new TenantAccessException(TenantFailureKind.Disabled, "Compliant Device");
        _gateway.SetReal(A, "Busy", "InACall");

        var snapshot = await Poll();

        Assert.Equal(TenantHealth.Disabled, Tenant(snapshot, C).Health);
        Assert.True(_gateway.Sessions.ContainsKey(B));
    }

    [Fact]
    public async Task Unexpected_exception_is_treated_as_transient()
    {
        var throwing = new ThrowingGateway(_gateway, B);
        var engine = new SyncEngine(throwing, SyncOptions.Default, _time);
        await engine.ConfigureAsync([new(A, "A", true), new(B, "B", true)], SyncOptions.Default, CancellationToken.None);

        var snapshot = await engine.PollAsync(CancellationToken.None);

        Assert.Equal(TenantHealth.Degraded, snapshot.Tenants.Single(t => t.TenantId == B).Health);
        Assert.Equal(TenantHealth.Ok, snapshot.Tenants.Single(t => t.TenantId == A).Health);
    }

    [Fact]
    public async Task Auto_sync_off_clears_sessions_and_sets_nothing()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        await _engine.SetAutoSyncAsync(false, CancellationToken.None);
        var snapshot = await Poll();

        Assert.Empty(_gateway.Sessions);
        Assert.Equal(TrayState.Off, snapshot.TrayState);

        _time.Advance(TimeSpan.FromSeconds(20));
        await Poll();
        Assert.Equal(1, _gateway.CountCalls("set:tenant-b"));
    }

    [Fact]
    public async Task Disabling_a_tenant_clears_its_session()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        await _engine.ConfigureAsync(
            [new(A, "Kunde A", true), new(B, "Kunde B", false), new(C, "LEVEL21", true)],
            SyncOptions.Default, CancellationToken.None);

        Assert.False(_gateway.Sessions.ContainsKey(B));
        Assert.True(_gateway.Sessions.ContainsKey(C));
    }

    [Fact]
    public async Task Shutdown_cleanup_clears_all_own_sessions()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        await _engine.ClearOwnSessionsAsync(CancellationToken.None);

        Assert.Empty(_gateway.Sessions);
    }

    [Fact]
    public async Task Expired_session_is_forgotten_and_not_a_source()
    {
        _gateway.SetReal(A, "Busy", "InACall");
        await Poll();

        // App was suspended: A's call ended, our sessions expired on the server.
        _gateway.SetReal(A, "Available", "Available");
        _gateway.Sessions.Clear();
        _time.Advance(TimeSpan.FromMinutes(6));
        var snapshot = await Poll();

        Assert.All(snapshot.Tenants, t => Assert.Null(t.OwnSession));
        Assert.Equal(0, _gateway.CountCalls("clear:"));
    }

    private sealed class ThrowingGateway(IPresenceGateway inner, string failingTenant) : IPresenceGateway
    {
        public Task<PresenceInfo> GetPresenceAsync(string tenantId, CancellationToken ct) =>
            tenantId == failingTenant ? throw new InvalidOperationException("boom") : inner.GetPresenceAsync(tenantId, ct);

        public Task SetSessionAsync(string tenantId, SyncKind kind, TimeSpan expiration, CancellationToken ct) =>
            inner.SetSessionAsync(tenantId, kind, expiration, ct);

        public Task ClearSessionAsync(string tenantId, CancellationToken ct) => inner.ClearSessionAsync(tenantId, ct);

        public Task SetPreferredAsync(string tenantId, ManualStatus status, TimeSpan? expiration, CancellationToken ct) =>
            inner.SetPreferredAsync(tenantId, status, expiration, ct);

        public Task ClearPreferredAsync(string tenantId, CancellationToken ct) => inner.ClearPreferredAsync(tenantId, ct);
    }
}
