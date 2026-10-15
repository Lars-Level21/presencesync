namespace PresenceSync.Core.Presence;

/// <summary>German display texts for tray menu and tooltip.</summary>
public static class PresenceText
{
    public static string Describe(PresenceInfo? presence)
    {
        if (presence is null)
            return "Unbekannt";

        var activity = presence.Activity.ToLowerInvariant() switch
        {
            "inacall" => "Im Anruf",
            "inaconferencecall" => "In Telefonkonferenz",
            "inameeting" => "In Besprechung",
            "presenting" => "Präsentiert",
            "urgentinterruptionsonly" => "Nur dringende Unterbrechungen",
            "outofoffice" => "Abwesenheitsnotiz",
            "offwork" => "Feierabend",
            _ => null,
        };
        return activity ?? Describe(presence.Availability);
    }

    public static string Describe(string availability) => availability.ToLowerInvariant() switch
    {
        "available" => "Verfügbar",
        "availableidle" => "Verfügbar (inaktiv)",
        "busy" => "Beschäftigt",
        "busyidle" => "Beschäftigt (inaktiv)",
        "donotdisturb" => "Nicht stören",
        "berightback" => "Gleich zurück",
        "away" => "Abwesend",
        "offline" => "Offline",
        _ => "Unbekannt",
    };

    public static string Describe(ManualStatus status) => status switch
    {
        ManualStatus.Available => "Verfügbar",
        ManualStatus.Busy => "Beschäftigt",
        ManualStatus.DoNotDisturb => "Nicht stören",
        ManualStatus.BeRightBack => "Gleich zurück",
        ManualStatus.Away => "Abwesend",
        _ => status.ToString(),
    };
}
