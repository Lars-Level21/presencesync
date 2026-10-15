namespace PresenceSync.Core.Presence;

/// <summary>Aggregated Teams presence of the signed-in user in one tenant (GET /me/presence).</summary>
public sealed record PresenceInfo(string Availability, string Activity)
{
    public static PresenceInfo Unknown { get; } = new(Availabilities.PresenceUnknown, Activities.PresenceUnknown);

    public bool Is(string availability, string activity) =>
        string.Equals(Availability, availability, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Activity, activity, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"{Availability}/{Activity}";
}

public static class Availabilities
{
    public const string Available = "Available";
    public const string AvailableIdle = "AvailableIdle";
    public const string Away = "Away";
    public const string BeRightBack = "BeRightBack";
    public const string Busy = "Busy";
    public const string BusyIdle = "BusyIdle";
    public const string DoNotDisturb = "DoNotDisturb";
    public const string Offline = "Offline";
    public const string PresenceUnknown = "PresenceUnknown";
}

public static class Activities
{
    public const string Available = "Available";
    public const string Away = "Away";
    public const string BeRightBack = "BeRightBack";
    public const string Busy = "Busy";
    public const string DoNotDisturb = "DoNotDisturb";
    public const string InACall = "InACall";
    public const string InAConferenceCall = "InAConferenceCall";
    public const string InAMeeting = "InAMeeting";
    public const string Inactive = "Inactive";
    public const string OffWork = "OffWork";
    public const string OutOfOffice = "OutOfOffice";
    public const string Presenting = "Presenting";
    public const string UrgentInterruptionsOnly = "UrgentInterruptionsOnly";
    public const string PresenceUnknown = "PresenceUnknown";
}
