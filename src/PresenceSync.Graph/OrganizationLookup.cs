using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace PresenceSync.Graph;

/// <summary>Reads the tenant's display name (GET /organization, allowed with User.Read) as a default tenant name.</summary>
public static class OrganizationLookup
{
    /// <summary>Best effort: returns null on any error, the caller falls back to the user's domain.</summary>
    public static async Task<string?> GetDisplayNameAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, GraphClient.BaseUrl + "organization?$select=displayName");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return null;
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            var name = json?["value"]?[0]?["displayName"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }
}
