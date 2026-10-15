using Microsoft.Identity.Client.Extensions.Msal;

namespace PresenceSync.Graph;

/// <summary>
/// Persistent MSAL cache (account metadata, ID tokens; with WAM the refresh tokens stay in the broker).
/// Stored DPAPI-encrypted for the current Windows user under %LOCALAPPDATA%\PresenceSync.
/// </summary>
public static class TokenCache
{
    public static Task<MsalCacheHelper> CreateAsync()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PresenceSync");
        Directory.CreateDirectory(directory);
        var properties = new StorageCreationPropertiesBuilder("msal.cache", directory).Build();
        return MsalCacheHelper.CreateAsync(properties);
    }
}
