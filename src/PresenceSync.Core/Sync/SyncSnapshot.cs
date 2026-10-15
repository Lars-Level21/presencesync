using PresenceSync.Core.Resources;
using PresenceSync.Core.Presence;

namespace PresenceSync.Core.Sync;

public sealed record TenantDescriptor(string TenantId, string Name, bool Enabled);

public sealed record TenantSnapshot(
    string TenantId,
    string Name,
    bool Enabled,
    PresenceInfo? Presence,
    TenantRole Role,
    TenantHealth Health,
    string? HealthMessage,
    ManualState? Manual,
    OwnSession? OwnSession,
    DateTimeOffset? BackoffUntil)
{
    public bool HasProblem => Enabled && Health is TenantHealth.AuthRequired or TenantHealth.Disabled;

    /// <summary>Short line for tooltip and menu in the UI language, e.g. "In a call (source)".</summary>
    public string StatusLine
    {
        get
        {
            if (!Enabled) return Strings.Tenant_Disabled;
            return Health switch
            {
                TenantHealth.AuthRequired => Strings.Tenant_SignInRequired,
                TenantHealth.Disabled => Strings.Format(nameof(Strings.Tenant_Blocked), HealthMessage),
                TenantHealth.Unknown => Strings.Tenant_Pending,
                _ => WithRole(PresenceText.Describe(Presence)) + (Health == TenantHealth.Degraded ? " ⚠" : ""),
            };
        }
    }

    private string WithRole(string status) => Role switch
    {
        TenantRole.Source => Strings.Format(nameof(Strings.Tenant_RoleSource), status),
        TenantRole.Synced => Strings.Format(nameof(Strings.Tenant_RoleSynced), status),
        TenantRole.Manual => Strings.Format(nameof(Strings.Tenant_RoleManual), status),
        _ => status,
    };
}

public enum TrayState
{
    /// <summary>Green: everything available.</summary>
    Available,

    /// <summary>Red: occupied somewhere / sync active.</summary>
    Busy,

    /// <summary>Grey: auto-sync off.</summary>
    Off,

    /// <summary>Yellow: at least one tenant with an auth problem.</summary>
    Warning,
}

public sealed record SyncSnapshot(DateTimeOffset At, bool AutoSync, IReadOnlyList<TenantSnapshot> Tenants)
{
    public TrayState TrayState
    {
        get
        {
            if (Tenants.Any(t => t.HasProblem))
                return TrayState.Warning;
            if (!AutoSync)
                return TrayState.Off;
            if (Tenants.Any(t => t.Enabled && (t.Role is TenantRole.Source or TenantRole.Synced || PresenceRules.IsOccupied(t.Presence))))
                return TrayState.Busy;
            return TrayState.Available;
        }
    }
}
