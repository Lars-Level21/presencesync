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
    /// <summary>Green: at least one tenant available, none busy (or no presence known yet).</summary>
    Available,

    /// <summary>Red: occupied or busy somewhere / sync active.</summary>
    Busy,

    /// <summary>Grey: auto-sync off.</summary>
    Off,

    /// <summary>Yellow: at least one tenant with an auth problem.</summary>
    Warning,

    /// <summary>Yellow clock: no tenant available or busy, at least one away / be right back.</summary>
    Away,

    /// <summary>Grey: every tenant with a known presence is offline.</summary>
    Offline,
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

            // Nothing occupied: summarize the known availabilities, the "most present" one wins.
            var known = KnownAvailabilities.ToList();
            if (known.Count == 0)
                return TrayState.Available; // no presence read yet (start-up)
            if (known.Any(a => Is(a, Availabilities.Busy, Availabilities.BusyIdle, Availabilities.DoNotDisturb)))
                return TrayState.Busy;
            if (known.Any(a => Is(a, Availabilities.Available, Availabilities.AvailableIdle)))
                return TrayState.Available;
            if (known.Any(a => Is(a, Availabilities.Away, Availabilities.BeRightBack)))
                return TrayState.Away;
            return TrayState.Offline;
        }
    }

    /// <summary>True if every enabled tenant with a known presence is available (for "All available").</summary>
    public bool AllAvailable =>
        KnownAvailabilities.All(a => Is(a, Availabilities.Available, Availabilities.AvailableIdle));

    private IEnumerable<string> KnownAvailabilities =>
        Tenants
            .Where(t => t.Enabled && t.Presence is not null)
            .Select(t => t.Presence!.Availability)
            .Where(a => !string.IsNullOrEmpty(a) && !Is(a, Availabilities.PresenceUnknown));

    private static bool Is(string availability, params string[] candidates) =>
        candidates.Any(c => string.Equals(availability, c, StringComparison.OrdinalIgnoreCase));
}
