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

    [GeneratedRegex(@"AADSTS\d{5,6}")]
    private static partial Regex CodePattern();
}
