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

    /// <summary>Short line for tooltip and menu, e.g. "Im Anruf (Quelle)".</summary>
    public string StatusLine
    {
        get
        {
            if (!Enabled) return "Deaktiviert";
            return Health switch
            {
                TenantHealth.AuthRequired => "Anmeldung erforderlich",
                TenantHealth.Disabled => "Gesperrt: " + HealthMessage,
                TenantHealth.Unknown => "…",
                _ => PresenceText.Describe(Presence) + Role switch
                {
                    TenantRole.Source => " (Quelle)",
                    TenantRole.Synced => " (Sync)",
                    TenantRole.Manual => " (manuell)",
                    _ => "",
                } + (Health == TenantHealth.Degraded ? " ⚠" : ""),
            };
        }
    }
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
