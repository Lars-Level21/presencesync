using PresenceSync.Core.Presence;

namespace PresenceSync.Core.Sync;

public enum TenantHealth
{
    /// <summary>Not polled yet.</summary>
    Unknown,
    Ok,

    /// <summary>Last call failed (throttling, network). Retried after backoff.</summary>
    Degraded,
    AuthRequired,
    Disabled,
}

public enum TenantRole
{
    None,

    /// <summary>Tenant is occupied by itself (call, meeting, DnD) and drives the sync.</summary>
    Source,

    /// <summary>The app holds its own setPresence session here.</summary>
    Synced,

    /// <summary>A manual status set through the app is active.</summary>
    Manual,
}

/// <summary>The app's own setPresence session in a tenant.</summary>
public sealed record OwnSession(SyncKind Kind, DateTimeOffset Since, DateTimeOffset Until);

/// <summary>A manual (preferred) status set through the app.</summary>
public sealed record ManualState(ManualStatus Status, DateTimeOffset Until);

/// <summary>
/// "Don't treat this tenant as a source before <see cref="Until"/> if its presence matches
/// <see cref="Presence"/> (null = any presence)". Covers the delay until Graph reflects a cleared state.
/// </summary>
public sealed record SourceSuppression(DateTimeOffset Until, PresenceInfo? Presence)
{
    public bool Applies(PresenceInfo? current, DateTimeOffset now) =>
        now < Until && (Presence is null || (current is not null && current.Is(Presence.Availability, Presence.Activity)));
}

/// <summary>Mutable per-tenant runtime state. Owned by <see cref="SyncEngine"/>, only touched under its lock.</summary>
internal sealed class TenantState(string tenantId, string name, bool enabled)
{
    public string TenantId { get; } = tenantId;
    public string Name { get; set; } = name;
    public bool Enabled { get; set; } = enabled;

    public PresenceInfo? Presence { get; set; }
    public DateTimeOffset? PresenceReadAt { get; set; }
    public OwnSession? OwnSession { get; set; }
    public ManualState? Manual { get; set; }
    public SourceSuppression? Suppression { get; set; }
    public bool IsSource { get; set; }

    public TenantHealth Health { get; set; } = TenantHealth.Unknown;
    public string? HealthMessage { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? BackoffUntil { get; set; }

    /// <summary>AuthRequired/Disabled tenants wait for a user action (sign-in, re-enable).</summary>
    public bool IsBlocked => Health is TenantHealth.AuthRequired or TenantHealth.Disabled;

    public bool CanCall(DateTimeOffset now) => Enabled && !IsBlocked && (BackoffUntil is null || BackoffUntil <= now);
}
