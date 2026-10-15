using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PresenceSync.Core.Sync;

namespace PresenceSync.Graph;

/// <summary>
/// Thin Microsoft Graph client on top of HttpClient.
/// </summary>
/// <remarks>
/// Why not the Microsoft.Graph SDK v5: the app needs six endpoints (/me/presence and five presence actions).
/// The SDK pulls in Kiota plus the whole generated Graph model (~40 MB of assemblies) into the single-file
/// package, slows down startup of a tray app and hides the raw HTTP response, which we need anyway to read
/// Retry-After and map status codes to <see cref="TenantFailureKind"/>. A small HttpClient wrapper is easier
/// to reason about here. Revisit if Phase 2 grows beyond calendarView + event CRUD.
/// </remarks>
internal sealed class GraphClient(HttpClient http, Func<bool, CancellationToken, Task<string>> getToken)
{
    public const string BaseUrl = "https://graph.microsoft.com/v1.0/";

    public async Task<JsonNode?> GetAsync(string path, CancellationToken ct) =>
        await SendAsync(HttpMethod.Get, path, null, ct);

    public Task<JsonNode?> PostAsync(string path, object? body, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, body, ct);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var forceRefresh = false;
        for (var attempt = 0; ; attempt++)
        {
            var token = await getToken(forceRefresh, ct);
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
                request.Content = JsonContent.Create(body);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new TenantAccessException(TenantFailureKind.Transient, "Netzwerkfehler", inner: ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new TenantAccessException(TenantFailureKind.Transient, "Zeitüberschreitung bei Graph", inner: ex);
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                    return string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);

                // Expired/revoked token: refresh once, then give up.
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    forceRefresh = true;
                    continue;
                }

                throw ToException(response, content);
            }
        }
    }

    private static TenantAccessException ToException(HttpResponseMessage response, string content)
    {
        var status = (int)response.StatusCode;
        var code = ReadErrorCode(content);
        var detail = code is null ? $"HTTP {status}" : $"HTTP {status} {code}";

        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                new TenantAccessException(TenantFailureKind.Throttled, $"Graph drosselt / nicht verfügbar ({detail})", RetryAfter(response)),
            HttpStatusCode.Unauthorized =>
                new TenantAccessException(TenantFailureKind.AuthRequired, $"Token abgelehnt ({detail})"),
            HttpStatusCode.Forbidden =>
                new TenantAccessException(TenantFailureKind.Disabled, $"Zugriff verweigert – Consent oder Teams-Lizenz prüfen ({detail})"),
            HttpStatusCode.NotFound =>
                new GraphNotFoundException(detail),
            _ => new TenantAccessException(TenantFailureKind.Transient, $"Graph-Fehler ({detail})"),
        };
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
            return date - DateTimeOffset.UtcNow;
        return null;
    }

    private static string? ReadErrorCode(string content)
    {
        try
        {
            return JsonNode.Parse(content)?["error"]?["code"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>404 from Graph. For clearPresence it means "no session", which is fine.</summary>
internal sealed class GraphNotFoundException(string detail)
    : TenantAccessException(TenantFailureKind.Transient, $"Nicht gefunden ({detail})");
