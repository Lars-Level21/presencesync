namespace PresenceSync.Core.Diagnostics;

/// <summary>Masks personal data before it is written to the log.</summary>
public static class Mask
{
    /// <summary>"lars.martin@customer-a.de" → "la***@cu***.de"</summary>
    public static string Upn(string? upn)
    {
        if (string.IsNullOrWhiteSpace(upn))
            return "";

        var at = upn.IndexOf('@');
        if (at < 0)
            return Part(upn);

        var domain = upn[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        var maskedDomain = dot > 0 ? Part(domain[..dot]) + domain[dot..] : Part(domain);
        return $"{Part(upn[..at])}@{maskedDomain}";
    }

    private static string Part(string value) => value.Length <= 2 ? "***" : value[..2] + "***";
}
