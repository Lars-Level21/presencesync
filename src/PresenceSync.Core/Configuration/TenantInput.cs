using System.Text.RegularExpressions;

namespace PresenceSync.Core.Configuration;

/// <summary>Validates what the user types as tenant: a tenant ID (GUID) or a verified domain.</summary>
public static partial class TenantInput
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();
        return Guid.TryParse(value, out _) || Domain().IsMatch(value);
    }

    [GeneratedRegex(@"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.IgnoreCase)]
    private static partial Regex Domain();
}
