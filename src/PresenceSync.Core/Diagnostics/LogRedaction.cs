using System.Text.RegularExpressions;

namespace PresenceSync.Core.Diagnostics;

/// <summary>
/// Last line of defence for the log file: masks e-mail addresses/UPNs and removes anything that looks like a token,
/// including text inside exception messages from MSAL or HTTP that the app does not control.
/// </summary>
public static partial class LogRedaction
{
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        text = Bearer().Replace(text, "Bearer [TOKEN]");
        text = Jwt().Replace(text, "[TOKEN]");
        return Email().Replace(text, m => Mask.Upn(m.Value));
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // JWT access/ID tokens: three base64url segments, header always starts with "eyJ".
    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._~+/\-]+=*")]
    private static partial Regex Bearer();
}
