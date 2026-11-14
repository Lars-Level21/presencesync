using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Tests;

public sealed class TrayStateTests
{
    private static TenantSnapshot Tenant(string? availability, bool enabled = true, TenantHealth health = TenantHealth.Ok) =>
        new(Guid.NewGuid().ToString(), "T", enabled,
            availability is null ? null : new PresenceInfo(availability, availability),
            TenantRole.None, health, null, null, null, null);

    private static SyncSnapshot Snapshot(params TenantSnapshot[] tenants) => new(DateTimeOffset.UnixEpoch, true, tenants);

    [Fact]
    public void All_away_is_away() =>
        Assert.Equal(TrayState.Away, Snapshot(Tenant("Away"), Tenant("Away")).TrayState);

    [Fact]
    public void Be_right_back_and_offline_is_away() =>
        Assert.Equal(TrayState.Away, Snapshot(Tenant("BeRightBack"), Tenant("Offline")).TrayState);

    [Fact]
    public void One_available_wins_over_away()
    {
        var snapshot = Snapshot(Tenant("Available"), Tenant("Away"));
        Assert.Equal(TrayState.Available, snapshot.TrayState);
        Assert.False(snapshot.AllAvailable);
    }

    [Fact]
    public void All_available_is_available()
    {
        var snapshot = Snapshot(Tenant("Available"), Tenant("AvailableIdle"));
        Assert.Equal(TrayState.Available, snapshot.TrayState);
        Assert.True(snapshot.AllAvailable);
    }

    [Fact]
    public void Manual_busy_without_call_is_busy() =>
        Assert.Equal(TrayState.Busy, Snapshot(Tenant("Busy"), Tenant("Away")).TrayState);

    [Fact]
    public void All_offline_is_offline() =>
        Assert.Equal(TrayState.Offline, Snapshot(Tenant("Offline"), Tenant("PresenceUnknown")).TrayState);

    [Fact]
    public void Disabled_and_unread_tenants_are_ignored() =>
        Assert.Equal(TrayState.Away, Snapshot(Tenant("Away"), Tenant("Available", enabled: false), Tenant(null, health: TenantHealth.Unknown)).TrayState);

    [Fact]
    public void Nothing_read_yet_stays_available() =>
        Assert.Equal(TrayState.Available, Snapshot(Tenant(null, health: TenantHealth.Unknown)).TrayState);

    [Fact]
    public void Auth_problem_still_wins() =>
        Assert.Equal(TrayState.Warning, Snapshot(Tenant("Away"), Tenant(null, health: TenantHealth.AuthRequired)).TrayState);
}
