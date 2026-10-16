namespace PresenceSync.Core.Configuration;

/// <summary>Contents of %APPDATA%\PresenceSync\config.json.</summary>
public sealed class AppConfig
{
    public string ClientId { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 20;
    public int SessionExpirationMinutes { get; set; } = 5;

    /// <summary>
    /// After the app cleared its own session in a tenant, Graph can still report the old Busy state for a short
    /// while. During this window that stale state is not treated as a new sync source (prevents ping-pong).
    /// </summary>
    public int SourceCooldownSeconds { get; set; } = 60;

    public bool AutoSync { get; set; } = true;

    /// <summary>Use the Windows broker (WAM). If false or WAM is unavailable, MSAL uses the system browser.</summary>
    public bool UseBroker { get; set; } = true;

    public bool Autostart { get; set; } = true;
    public List<TenantConfig> Tenants { get; set; } = [];
    public HotkeyConfig Hotkeys { get; set; } = new();
    public CalendarMirrorConfig CalendarMirror { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 10, 300));
}

public sealed class TenantConfig
{
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Upn { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool IsHome { get; set; }
}

public sealed class HotkeyConfig
{
    public bool Enabled { get; set; }

    /// <summary>"All → Do not disturb 1 h". Format: modifiers + key, e.g. "Ctrl+Alt+D".</summary>
    public string DoNotDisturbOneHour { get; set; } = "Ctrl+Alt+D";

    /// <summary>"All → Reset".</summary>
    public string Reset { get; set; } = "Ctrl+Alt+R";
}

/// <summary>Phase 2, not implemented yet. Kept so the config format is stable.</summary>
public sealed class CalendarMirrorConfig
{
    public bool Enabled { get; set; }
    public int DaysAhead { get; set; } = 14;
    public int IntervalMinutes { get; set; } = 10;
    public string BlockerSubject { get; set; } = "Blocker";
    public bool DryRun { get; set; } = true;
}
