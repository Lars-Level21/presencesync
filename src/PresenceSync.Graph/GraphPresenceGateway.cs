using System.Collections.Concurrent;
using System.Xml;
using Microsoft.Extensions.Logging;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;

namespace PresenceSync.Graph;

/// <summary>
/// <see cref="IPresenceGateway"/> on Microsoft Graph v1.0 with delegated Presence.ReadWrite.
/// One <see cref="TenantAuthenticator"/> per tenant; the user's object ID is taken from GET /me/presence.
/// </summary>
public sealed class GraphPresenceGateway : IPresenceGateway, IDisposable
{
    private readonly string _clientId;
    private readonly HttpClient _http;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, TenantContext> _tenants = new(StringComparer.OrdinalIgnoreCase);

    public GraphPresenceGateway(string clientId, ILogger<GraphPresenceGateway> log)
    {
        _clientId = clientId;
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PresenceSync/0.1");
    }

    public void Register(string tenantId, TenantAuthenticator auth)
    {
        var client = new GraphClient(_http, auth.GetTokenSilentAsync);
        _tenants[tenantId] = new TenantContext(auth, client);
    }

    public void Unregister(string tenantId) => _tenants.TryRemove(tenantId, out _);

    public async Task<PresenceInfo> GetPresenceAsync(string tenantId, CancellationToken ct)
    {
        var ctx = Get(tenantId);
        var json = await ctx.Client.GetAsync("me/presence", ct);
        ctx.UserId ??= json?["id"]?.GetValue<string>();
        return new PresenceInfo(
            json?["availability"]?.GetValue<string>() ?? Availabilities.PresenceUnknown,
            json?["activity"]?.GetValue<string>() ?? Activities.PresenceUnknown);
    }

    public async Task SetSessionAsync(string tenantId, SyncKind kind, TimeSpan expiration, CancellationToken ct)
    {
        var ctx = Get(tenantId);
        var presence = PresenceRules.SessionPresence(kind);
        await ctx.Client.PostAsync($"users/{Uri.EscapeDataString(await UserIdAsync(ctx, ct))}/presence/setPresence", new
        {
            sessionId = _clientId,
            availability = presence.Availability,
            activity = presence.Activity,
            expirationDuration = XmlConvert.ToString(PresenceRules.ClampSessionExpiration(expiration)),
        }, ct);
    }

    public async Task ClearSessionAsync(string tenantId, CancellationToken ct)
    {
        var ctx = Get(tenantId);
        try
        {
            await ctx.Client.PostAsync($"users/{Uri.EscapeDataString(await UserIdAsync(ctx, ct))}/presence/clearPresence",
                new { sessionId = _clientId }, ct);
        }
        catch (GraphNotFoundException)
        {
            _log.LogDebug("clearPresence: keine Session vorhanden ({TenantId})", tenantId);
        }
    }

    public async Task SetPreferredAsync(string tenantId, ManualStatus status, TimeSpan? expiration, CancellationToken ct)
    {
        var ctx = Get(tenantId);
        var presence = status.ToPreferredPresence();
        object body = expiration is { } e
            ? new { availability = presence.Availability, activity = presence.Activity, expirationDuration = XmlConvert.ToString(e) }
            : new { availability = presence.Availability, activity = presence.Activity };
        await ctx.Client.PostAsync($"users/{Uri.EscapeDataString(await UserIdAsync(ctx, ct))}/presence/setUserPreferredPresence", body, ct);
    }

    public async Task ClearPreferredAsync(string tenantId, CancellationToken ct)
    {
        var ctx = Get(tenantId);
        await ctx.Client.PostAsync($"users/{Uri.EscapeDataString(await UserIdAsync(ctx, ct))}/presence/clearUserPreferredPresence", null, ct);
    }

    public void Dispose() => _http.Dispose();

    private TenantContext Get(string tenantId) =>
        _tenants.TryGetValue(tenantId, out var ctx)
            ? ctx
            : throw new TenantAccessException(TenantFailureKind.Disabled, "Tenant nicht konfiguriert");

    private static async Task<string> UserIdAsync(TenantContext ctx, CancellationToken ct)
    {
        if (ctx.UserId is null)
        {
            var me = await ctx.Client.GetAsync("me?$select=id", ct);
            ctx.UserId = me?["id"]?.GetValue<string>()
                         ?? throw new TenantAccessException(TenantFailureKind.Transient, "Benutzer-ID nicht ermittelbar");
        }
        return ctx.UserId;
    }

    private sealed class TenantContext(TenantAuthenticator auth, GraphClient client)
    {
        public TenantAuthenticator Auth { get; } = auth;
        public GraphClient Client { get; } = client;
        public string? UserId { get; set; }
    }
}
