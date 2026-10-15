using System.Text.RegularExpressions;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Diagnostics;

/// <summary>
/// Maps Entra ID error codes (AADSTSxxxxx) to a failure kind and a German plain-text message for tray and log.
/// AuthRequired = an interactive sign-in can fix it; Disabled = the tenant cannot be used until something changes
/// on the tenant side (Conditional Access, consent policy, ...).
/// </summary>
public static partial class AadErrorCatalog
{
    private static readonly Dictionary<string, (TenantFailureKind Kind, string Text)> Known = new()
    {
        ["AADSTS50076"] = (TenantFailureKind.AuthRequired, "MFA erforderlich – bitte anmelden"),
        ["AADSTS50079"] = (TenantFailureKind.AuthRequired, "MFA-Registrierung erforderlich – bitte anmelden"),
        ["AADSTS50078"] = (TenantFailureKind.AuthRequired, "MFA abgelaufen – bitte anmelden"),
        ["AADSTS50173"] = (TenantFailureKind.AuthRequired, "Anmeldung abgelaufen (Passwort geändert?) – bitte anmelden"),
        ["AADSTS700082"] = (TenantFailureKind.AuthRequired, "Anmeldung abgelaufen (Inaktivität) – bitte anmelden"),
        ["AADSTS70043"] = (TenantFailureKind.AuthRequired, "Sitzung durch Conditional Access abgelaufen – bitte anmelden"),
        ["AADSTS50058"] = (TenantFailureKind.AuthRequired, "Keine Anmeldesitzung vorhanden – bitte anmelden"),
        ["AADSTS65001"] = (TenantFailureKind.AuthRequired, "Zustimmung (Consent) fehlt – bitte anmelden und zustimmen"),
        ["AADSTS53000"] = (TenantFailureKind.Disabled, "Conditional Access verlangt ein konformes (compliant) Gerät"),
        ["AADSTS53001"] = (TenantFailureKind.Disabled, "Conditional Access verlangt ein in die Domäne eingebundenes Gerät"),
        ["AADSTS53002"] = (TenantFailureKind.Disabled, "Conditional Access erlaubt diese App nicht (genehmigte Client-App verlangt)"),
        ["AADSTS53003"] = (TenantFailureKind.Disabled, "Zugriff durch Conditional Access blockiert"),
        ["AADSTS530003"] = (TenantFailureKind.Disabled, "Conditional Access verlangt ein verwaltetes Gerät"),
        ["AADSTS90094"] = (TenantFailureKind.Disabled, "Administratorgenehmigung erforderlich – Kunden-IT anfragen, siehe docs/consent.md"),
        ["AADSTS90008"] = (TenantFailureKind.Disabled, "Administratorgenehmigung erforderlich – Kunden-IT anfragen, siehe docs/consent.md"),
        ["AADSTS65004"] = (TenantFailureKind.Disabled, "Zustimmung (Consent) wurde abgelehnt"),
        ["AADSTS700016"] = (TenantFailureKind.Disabled, "App-Registrierung im Tenant nicht gefunden (Client-ID prüfen)"),
        ["AADSTS50105"] = (TenantFailureKind.Disabled, "Benutzer ist der App im Tenant nicht zugewiesen (Assignment required)"),
        ["AADSTS50020"] = (TenantFailureKind.Disabled, "Konto existiert in diesem Tenant nicht"),
        ["AADSTS50034"] = (TenantFailureKind.Disabled, "Konto existiert in diesem Tenant nicht"),
        ["AADSTS50057"] = (TenantFailureKind.Disabled, "Konto ist deaktiviert"),
        ["AADSTS50053"] = (TenantFailureKind.Disabled, "Konto ist gesperrt"),
        ["AADSTS90072"] = (TenantFailureKind.Disabled, "Konto ist in diesem Tenant nicht vorhanden (Gast/Home-Tenant prüfen)"),
    };

    public static string? ExtractCode(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;
        var match = CodePattern().Match(message);
        return match.Success ? match.Value : null;
    }

    /// <summary>Returns null if the message contains no known AADSTS code.</summary>
    public static (TenantFailureKind Kind, string Text, string Code)? Classify(string? message)
    {
        var code = ExtractCode(message);
        if (code is null || !Known.TryGetValue(code, out var entry))
            return null;
        return (entry.Kind, $"{entry.Text} ({code})", code);
    }

    [GeneratedRegex(@"AADSTS\d{5,6}")]
    private static partial Regex CodePattern();
}
