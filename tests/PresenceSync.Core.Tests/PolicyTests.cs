using PresenceSync.Core.Configuration;

namespace PresenceSync.Core.Tests;

public sealed class PolicyTests
{
    private const string OrgClientId = "11111111-1111-1111-1111-111111111111";
    private const string OwnClientId = "22222222-2222-2222-2222-222222222222";
    private const string HomeTenant = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void No_policy_changes_nothing()
    {
        var config = new AppConfig { ClientId = OwnClientId };

        var state = PolicyApplier.Apply(config, PolicySettings.None);

        Assert.Equal(OwnClientId, config.ClientId);
        Assert.False(state.IsActive);
        Assert.Empty(config.Tenants);
    }

    [Fact]
    public void Policy_client_id_overrides_and_locks()
    {
        var config = new AppConfig { ClientId = OwnClientId };

        var state = PolicyApplier.Apply(config, new PolicySettings(OrgClientId, null, null));

        Assert.Equal(OrgClientId, config.ClientId);
        Assert.True(state.ClientIdLocked);
    }

    [Fact]
    public void Allow_custom_client_id_makes_policy_a_default_only()
    {
        var own = new AppConfig { ClientId = OwnClientId };
        var empty = new AppConfig();
        var policy = new PolicySettings(OrgClientId, null, AllowCustomClientId: true);

        var stateOwn = PolicyApplier.Apply(own, policy);
        PolicyApplier.Apply(empty, policy);

        Assert.Equal(OwnClientId, own.ClientId);
        Assert.Equal(OrgClientId, empty.ClientId);
        Assert.False(stateOwn.ClientIdLocked);
    }

    [Fact]
    public void Invalid_client_id_is_ignored_with_warning()
    {
        var config = new AppConfig { ClientId = OwnClientId };

        var state = PolicyApplier.Apply(config, new PolicySettings("not-a-guid", null, null));

        Assert.Equal(OwnClientId, config.ClientId);
        Assert.False(state.ClientIdLocked);
        Assert.Single(state.Warnings);
    }

    [Fact]
    public void Home_tenant_is_added_once_as_managed_home()
    {
        var config = new AppConfig { Tenants = [new TenantConfig { Name = "Customer A", TenantId = "44444444-4444-4444-4444-444444444444" }] };
        var policy = new PolicySettings(null, HomeTenant, null);

        var state = PolicyApplier.Apply(config, policy);
        PolicyApplier.Apply(config, policy);

        Assert.Equal(2, config.Tenants.Count);
        var home = config.Tenants[0];
        Assert.Equal(HomeTenant, home.TenantId);
        Assert.True(home.IsHome);
        Assert.True(home.Enabled);
        Assert.Equal("", home.Upn);
        Assert.True(state.IsManagedTenant(HomeTenant.ToUpperInvariant()));
    }

    [Fact]
    public void Existing_home_tenant_keeps_name_and_account()
    {
        var config = new AppConfig { Tenants = [new TenantConfig { Name = "LEVEL21", TenantId = HomeTenant, Upn = "lars@example.com" }] };

        PolicyApplier.Apply(config, new PolicySettings(null, HomeTenant, null));

        var home = Assert.Single(config.Tenants);
        Assert.Equal("LEVEL21", home.Name);
        Assert.Equal("lars@example.com", home.Upn);
        Assert.True(home.IsHome);
    }

    [Fact]
    public void Home_tenant_must_be_a_guid()
    {
        var config = new AppConfig();

        var state = PolicyApplier.Apply(config, new PolicySettings(null, "contoso.com", null));

        Assert.Empty(config.Tenants);
        Assert.Null(state.ManagedTenantId);
        Assert.Single(state.Warnings);
    }

    [Fact]
    public void Machine_policy_wins_over_user_policy_per_value()
    {
        var machine = new PolicySettings(OrgClientId, null, null);
        var user = new PolicySettings(OwnClientId, HomeTenant, true);

        var merged = PolicySettings.Merge(machine, user);

        Assert.Equal(OrgClientId, merged.ClientId);
        Assert.Equal(HomeTenant, merged.HomeTenantId);
        Assert.True(merged.AllowCustomClientId);
    }
}
