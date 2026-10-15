using PresenceSync.Core.Presence;

namespace PresenceSync.Core.Sync;

/// <summary>
/// Presence operations for the signed-in user in one tenant. Implemented by PresenceSync.Graph,
/// mocked in tests. Implementations must never show interactive UI and throw
/// <see cref="TenantAccessException"/> for anything the engine should react to.
/// </summary>
public interface IPresenceGateway
{
    /// <summary>GET /me/presence</summary>
    Task<PresenceInfo> GetPresenceAsync(string tenantId, CancellationToken ct);

    /// <summary>POST /users/{id}/presence/setPresence with sessionId = clientId.</summary>
    Task SetSessionAsync(string tenantId, SyncKind kind, TimeSpan expiration, CancellationToken ct);

    /// <summary>POST /users/{id}/presence/clearPresence. "Session not found" counts as success.</summary>
    Task ClearSessionAsync(string tenantId, CancellationToken ct);

    /// <summary>POST /users/{id}/presence/setUserPreferredPresence. <paramref name="expiration"/> null = Graph default.</summary>
    Task SetPreferredAsync(string tenantId, ManualStatus status, TimeSpan? expiration, CancellationToken ct);

    /// <summary>POST /users/{id}/presence/clearUserPreferredPresence</summary>
    Task ClearPreferredAsync(string tenantId, CancellationToken ct);
}

public enum TenantFailureKind
{
    /// <summary>Token cannot be acquired silently. User has to sign in from the tray menu.</summary>
    AuthRequired,

    /// <summary>Tenant cannot be used (Conditional Access, consent policy, missing license, ...).</summary>
    Disabled,

    /// <summary>HTTP 429/503. Respect <see cref="TenantAccessException.RetryAfter"/>.</summary>
    Throttled,

    /// <summary>Network error, 5xx, timeout. Retry with backoff.</summary>
    Transient,
}

public class TenantAccessException(
    TenantFailureKind kind,
    string userMessage,
    TimeSpan? retryAfter = null,
    Exception? inner = null)
    : Exception(userMessage, inner)
{
    public TenantFailureKind Kind { get; } = kind;

    /// <summary>German plain-text message for tray and log. Never contains tokens.</summary>
    public string UserMessage { get; } = userMessage;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
