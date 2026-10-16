using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using PresenceSync.Core.Resources;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Tests;

/// <summary>Temporarily switches the UI language of the current thread.</summary>
internal sealed class UICulture : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public UICulture(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}

public sealed partial class LocalizationTests
{
    private static readonly CultureInfo German = new("de");

    private static List<string> EnglishKeys() => Strings.Keys(CultureInfo.InvariantCulture).Order().ToList();

    [Fact]
    public void German_resources_contain_exactly_the_english_keys()
    {
        var german = Strings.Keys(German).Order().ToList();

        Assert.Equal(EnglishKeys(), german);
    }

    [Fact]
    public void Every_resource_key_has_a_generated_property_and_vice_versa()
    {
        var properties = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .Order()
            .ToList();

        Assert.Equal(EnglishKeys(), properties);
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        foreach (var key in EnglishKeys())
        {
            var english = Placeholders(Strings.Get(key, CultureInfo.InvariantCulture));
            var german = Placeholders(Strings.Get(key, German));
            Assert.True(english.SequenceEqual(german), $"{key}: placeholders differ ({string.Join(",", english)} vs {string.Join(",", german)})");
        }
    }

    [Fact]
    public void German_texts_are_actually_translated_where_expected()
    {
        Assert.Equal("Verfügbar", Strings.Get(nameof(Strings.Presence_Available), German));
        Assert.Equal("Available", Strings.Get(nameof(Strings.Presence_Available), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Localized_exception_shows_ui_language_and_logs_english()
    {
        using var _ = new UICulture("de-DE");

        var ex = new TenantAccessException(TenantFailureKind.Transient, new LocalizedMessage(nameof(Strings.Error_Network)));

        Assert.Equal("Netzwerkfehler", ex.UserMessage);
        Assert.Equal("Network error", ex.Message);
    }

    [Fact]
    public void Nested_messages_are_rendered_in_the_same_language()
    {
        var message = new LocalizedMessage(nameof(Strings.Error_WithCode), new LocalizedMessage(nameof(Strings.Aad_MfaRequired)), "AADSTS50076");

        using (new UICulture("de-DE"))
            Assert.Equal("MFA erforderlich – bitte anmelden (AADSTS50076)", message.Text);
        Assert.Equal("MFA required – please sign in (AADSTS50076)", message.LogText);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order();

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex PlaceholderPattern();
}
