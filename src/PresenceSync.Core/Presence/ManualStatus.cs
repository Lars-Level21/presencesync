namespace PresenceSync.Core.Presence;

/// <summary>Status the user sets by hand from the tray (setUserPreferredPresence).</summary>
public enum ManualStatus
{
    Available,
    Busy,
    DoNotDisturb,
    BeRightBack,
    Away,
}

public static class ManualStatusExtensions
{
    /// <summary>Documented availability/activity combinations for setUserPreferredPresence.</summary>
    public static PresenceInfo ToPreferredPresence(this ManualStatus status) => status switch
    {
        ManualStatus.Available => new(Availabilities.Available, Activities.Available),
        ManualStatus.Busy => new(Availabilities.Busy, Activities.Busy),
        ManualStatus.DoNotDisturb => new(Availabilities.DoNotDisturb, Activities.DoNotDisturb),
        ManualStatus.BeRightBack => new(Availabilities.BeRightBack, Activities.BeRightBack),
        ManualStatus.Away => new(Availabilities.Away, Activities.Away),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    /// <summary>
    /// Graph applies a default expiration when none is sent: 1 day for Busy/DoNotDisturb, 7 days otherwise.
    /// "Unbegrenzt" in the tray therefore means "Graph default", not forever.
    /// </summary>
    public static TimeSpan GraphDefaultExpiration(this ManualStatus status) =>
        status is ManualStatus.Busy or ManualStatus.DoNotDisturb ? TimeSpan.FromDays(1) : TimeSpan.FromDays(7);
}
