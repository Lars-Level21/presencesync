using PresenceSync.Core.Resources;

namespace PresenceSync.Core.Presence;

/// <summary>Display texts for presence values in the UI language (tray menu, tooltip, flyout).</summary>
public static class PresenceText
{
    public static string Describe(PresenceInfo? presence)
    {
        if (presence is null)
            return Strings.Presence_Unknown;

        var activity = presence.Activity.ToLowerInvariant() switch
        {
            "inacall" => Strings.Presence_InACall,
            "inaconferencecall" => Strings.Presence_InAConferenceCall,
            "inameeting" => Strings.Presence_InAMeeting,
            "presenting" => Strings.Presence_Presenting,
            "urgentinterruptionsonly" => Strings.Presence_UrgentInterruptionsOnly,
            "outofoffice" => Strings.Presence_OutOfOffice,
            "offwork" => Strings.Presence_OffWork,
            _ => null,
        };
        return activity ?? Describe(presence.Availability);
    }

    public static string Describe(string availability) => availability.ToLowerInvariant() switch
    {
        "available" => Strings.Presence_Available,
        "availableidle" => Strings.Presence_AvailableIdle,
        "busy" => Strings.Presence_Busy,
        "busyidle" => Strings.Presence_BusyIdle,
        "donotdisturb" => Strings.Presence_DoNotDisturb,
        "berightback" => Strings.Presence_BeRightBack,
        "away" => Strings.Presence_Away,
        "offline" => Strings.Presence_Offline,
        _ => Strings.Presence_Unknown,
    };

    public static string Describe(ManualStatus status) => status switch
    {
        ManualStatus.Available => Strings.Presence_Available,
        ManualStatus.Busy => Strings.Presence_Busy,
        ManualStatus.DoNotDisturb => Strings.Presence_DoNotDisturb,
        ManualStatus.BeRightBack => Strings.Presence_BeRightBack,
        ManualStatus.Away => Strings.Presence_Away,
        _ => status.ToString(),
    };
}
