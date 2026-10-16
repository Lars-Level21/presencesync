using PresenceSync.Core.Resources;
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

/// <summary>
/// Failure of a tenant operation. <see cref="UserMessage"/> is shown in the UI language,
/// <see cref="Exception.Message"/> is English for the log. Never contains tokens.
/// </summary>
public class TenantAccessException : Exception
{
    public TenantAccessException(TenantFailureKind kind, LocalizedMessage message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message.LogText, inner)
    {
        Kind = kind;
        UserMessage = message.Text;
        RetryAfter = retryAfter;
    }

    /// <summary>Unlocalized message (tests, fakes): same text for UI and log.</summary>
    public TenantAccessException(TenantFailureKind kind, string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        UserMessage = message;
        RetryAfter = retryAfter;
    }

    public TenantFailureKind Kind { get; }

    /// <summary>Plain-text message in the UI language for tray and settings.</summary>
    public string UserMessage { get; }

    public TimeSpan? RetryAfter { get; }
}
