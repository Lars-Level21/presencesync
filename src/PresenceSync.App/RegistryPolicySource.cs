using Microsoft.Win32;
using PresenceSync.Core.Configuration;

namespace PresenceSync.App;

/// <summary>
/// Reads policies from HKLM and HKCU \SOFTWARE\Policies\PresenceSync (set via Intune, Group Policy/ADMX or script).
/// Users without admin rights cannot change the HKLM values.
/// </summary>
internal static class RegistryPolicySource
{
    public const string KeyPath = @"SOFTWARE\Policies\PresenceSync";

    public static PolicySettings Read() => PolicySettings.Merge(Read(Registry.LocalMachine), Read(Registry.CurrentUser));

    private static PolicySettings Read(RegistryKey hive)
    {
        try
        {
            using var key = hive.OpenSubKey(KeyPath);
            if (key is null)
                return PolicySettings.None;
            return new PolicySettings(
                key.GetValue("ClientId") as string,
                key.GetValue("HomeTenantId") as string,
                key.GetValue("AllowCustomClientId") is int allow ? allow != 0 : null);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return PolicySettings.None;
        }
    }
}
