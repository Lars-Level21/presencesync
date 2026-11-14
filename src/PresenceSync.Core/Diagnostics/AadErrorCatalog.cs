using System.Text.RegularExpressions;
using PresenceSync.Core.Resources;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Diagnostics;

/// <summary>
/// Maps Entra ID error codes (AADSTSxxxxx) to a failure kind and a plain-text message for tray and log.
/// AuthRequired = an interactive sign-in can fix it; Disabled = the tenant cannot be used until something changes
/// on the tenant side (Conditional Access, consent policy, ...).
/// </summary>
public static partial class AadErrorCatalog
{
    private static readonly Dictionary<string, (TenantFailureKind Kind, string Key)> Known = new()
    {
        ["AADSTS50076"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_MfaRequired)),
        ["AADSTS50079"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_MfaRegistrationRequired)),
        ["AADSTS50078"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_MfaExpired)),
        ["AADSTS50173"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_PasswordChanged)),
        ["AADSTS700082"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_Inactivity)),
        ["AADSTS70043"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_SessionExpiredByPolicy)),
        ["AADSTS50058"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_NoSession)),
        ["AADSTS65001"] = (TenantFailureKind.AuthRequired, nameof(Strings.Aad_ConsentMissing)),
        ["AADSTS53000"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_CompliantDevice)),
        ["AADSTS53001"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_DomainJoinedDevice)),
        ["AADSTS53002"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_ApprovedClientApp)),
        ["AADSTS53003"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_BlockedByPolicy)),
        ["AADSTS530003"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_ManagedDevice)),
        ["AADSTS90094"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AdminConsentRequired)),
        ["AADSTS90008"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AdminConsentRequired)),
        ["AADSTS65004"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_ConsentDeclined)),
        ["AADSTS700016"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AppNotFound)),
        ["AADSTS50105"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_NotAssigned)),
        ["AADSTS50020"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AccountNotInTenant)),
        ["AADSTS50034"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AccountNotInTenant)),
        ["AADSTS50057"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AccountDisabled)),
        ["AADSTS50053"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AccountLocked)),
        ["AADSTS90072"] = (TenantFailureKind.Disabled, nameof(Strings.Aad_AccountNotInTenantGuest)),
    };

    public static string? ExtractCode(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;
        var match = CodePattern().Match(message);
        return match.Success ? match.Value : null;
    }

    /// <summary>Returns null if the message contains no known AADSTS code. The message reads "text (AADSTSxxxxx)".</summary>
    public static (TenantFailureKind Kind, LocalizedMessage Message, string Code)? Classify(string? message)
    {
        var code = ExtractCode(message);
        if (code is null || !Known.TryGetValue(code, out var entry))
            return null;
        return (entry.Kind, new LocalizedMessage(nameof(Strings.Error_WithCode), new LocalizedMessage(entry.Key), code), code);
    }

    /// <summary>
    /// True for Windows broker (WAM) error codes that report a network problem rather than a sign-in problem:
    /// facility 0xCAA7xxxx (e.g. 0xCAA70004 "server temporarily unavailable", typical right after standby) and
    /// 0xCAA8xxxx (WinHTTP errors such as 0xCAA82EE2 timeout or 0xCAA82EFD cannot connect). A silent retry fixes
    /// these, so they must not ask the user for an interactive sign-in. MSAL reports the code as a decimal or hex string.
    /// </summary>
    public static bool IsBrokerNetworkError(string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            return false;
        var text = errorCode.Trim();
        uint code;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out code))
                return false;
        }
        else if (long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var value)
                 && value is >= int.MinValue and <= uint.MaxValue)
        {
            code = unchecked((uint)value); // a negative value is the same HRESULT read as a signed int
        }
        else
        {
            return false;
        }

        var high = code >> 16;
        return high is 0xCAA7 or 0xCAA8;
    }

    [GeneratedRegex(@"AADSTS\d{5,6}")]
    private static partial Regex CodePattern();
}
