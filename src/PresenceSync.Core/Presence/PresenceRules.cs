namespace PresenceSync.Core.Presence;

/// <summary>What the app writes into the other tenants via setPresence while a source is occupied.</summary>
public enum SyncKind
{
    /// <summary>Busy / InACall</summary>
    Busy,

    /// <summary>DoNotDisturb / Presenting</summary>
    DoNotDisturb,
}

public static class PresenceRules
{
    private static readonly HashSet<string> OccupiedActivities = new(StringComparer.OrdinalIgnoreCase)
    {
        Activities.InACall,
        Activities.InAConferenceCall,
        Activities.InAMeeting,
        Activities.Presenting,
        Activities.UrgentInterruptionsOnly,
    };

    /// <summary>A tenant is "occupied" (a sync source) when in a call/meeting, presenting or on DnD.</summary>
    public static bool IsOccupied(PresenceInfo? presence) =>
        presence is not null
        && (OccupiedActivities.Contains(presence.Activity)
            || string.Equals(presence.Availability, Availabilities.DoNotDisturb, StringComparison.OrdinalIgnoreCase));

    /// <summary>DnD and presenting in the source are mirrored as DoNotDisturb/Presenting, everything else as Busy/InACall.</summary>
    public static SyncKind KindFor(PresenceInfo presence) =>
        string.Equals(presence.Availability, Availabilities.DoNotDisturb, StringComparison.OrdinalIgnoreCase)
        || string.Equals(presence.Activity, Activities.Presenting, StringComparison.OrdinalIgnoreCase)
            ? SyncKind.DoNotDisturb
            : SyncKind.Busy;

    /// <summary>The availability/activity pair sent to setPresence. Only documented combinations are used.</summary>
    public static PresenceInfo SessionPresence(SyncKind kind) => kind switch
    {
        SyncKind.DoNotDisturb => new(Availabilities.DoNotDisturb, Activities.Presenting),
        _ => new(Availabilities.Busy, Activities.InACall),
    };

    /// <summary>setPresence accepts expirationDuration between PT5M and PT4H.</summary>
    public static TimeSpan ClampSessionExpiration(TimeSpan value) =>
        value < TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5)
        : value > TimeSpan.FromHours(4) ? TimeSpan.FromHours(4)
        : value;
}
