using PresenceSync.Core.Diagnostics;
using PresenceSync.Core.Presence;
using PresenceSync.Core.Sync;

namespace PresenceSync.Core.Tests;

public sealed class RulesAndDiagnosticsTests
{
    [Theory]
    [InlineData("Busy", "InACall", true)]
    [InlineData("Busy", "InAConferenceCall", true)]
    [InlineData("Busy", "InAMeeting", true)]
    [InlineData("DoNotDisturb", "Presenting", true)]
    [InlineData("DoNotDisturb", "UrgentInterruptionsOnly", true)]
    [InlineData("DoNotDisturb", "DoNotDisturb", true)]
    [InlineData("Busy", "Busy", false)]
    [InlineData("Available", "Available", false)]
    [InlineData("Away", "Away", false)]
    [InlineData("Offline", "OffWork", false)]
    public void IsOccupied(string availability, string activity, bool expected) =>
        Assert.Equal(expected, PresenceRules.IsOccupied(new PresenceInfo(availability, activity)));

    [Fact]
    public void Session_presence_uses_documented_combinations()
    {
        Assert.Equal(new PresenceInfo("Busy", "InACall"), PresenceRules.SessionPresence(SyncKind.Busy));
        Assert.Equal(new PresenceInfo("DoNotDisturb", "Presenting"), PresenceRules.SessionPresence(SyncKind.DoNotDisturb));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(30, 30)]
    [InlineData(500, 240)]
    public void Session_expiration_is_clamped(int minutes, int expected) =>
        Assert.Equal(TimeSpan.FromMinutes(expected), PresenceRules.ClampSessionExpiration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Preferred_presence_combinations()
    {
        Assert.Equal(new PresenceInfo("DoNotDisturb", "DoNotDisturb"), ManualStatus.DoNotDisturb.ToPreferredPresence());
        Assert.Equal(new PresenceInfo("BeRightBack", "BeRightBack"), ManualStatus.BeRightBack.ToPreferredPresence());
        Assert.Equal(TimeSpan.FromDays(1), ManualStatus.Busy.GraphDefaultExpiration());
        Assert.Equal(TimeSpan.FromDays(7), ManualStatus.Away.GraphDefaultExpiration());
    }

    [Theory]
    [InlineData("lars.martin@customer-a.de", "la***@cu***.de")]
    [InlineData("l@x.io", "***@***.io")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Upn_is_masked(string? upn, string expected) => Assert.Equal(expected, Mask.Upn(upn));

    [Fact]
    public void Conditional_access_compliant_device_disables_tenant()
    {
        var result = AadErrorCatalog.Classify("AADSTS53000: Device is not in required device state: compliant.");
        Assert.NotNull(result);
        Assert.Equal(TenantFailureKind.Disabled, result.Value.Kind);
        Assert.Equal("Conditional Access requires a compliant device (AADSTS53000)", result.Value.Message.LogText);
        using (new UICulture("de-DE"))
            Assert.Equal("Conditional Access verlangt ein konformes (compliant) Gerät (AADSTS53000)", result.Value.Message.Text);
    }

    [Fact]
    public void Mfa_requires_sign_in()
    {
        var result = AadErrorCatalog.Classify("MsalUiRequiredException: AADSTS50076: Due to a configuration change ...");
        Assert.Equal(TenantFailureKind.AuthRequired, result!.Value.Kind);
    }

    [Fact]
    public void Unknown_code_returns_null() => Assert.Null(AadErrorCatalog.Classify("AADSTS99999 whatever"));

    [Theory]
    [InlineData("en-US", "In a call (source)", "Busy (sync)", "Sign-in required")]
    [InlineData("de-DE", "Im Anruf (Quelle)", "Beschäftigt (Sync)", "Anmeldung erforderlich")]
    [InlineData("fr-FR", "In a call (source)", "Busy (sync)", "Sign-in required")] // unsupported language → English
    public void Status_line_follows_ui_language(string culture, string source, string synced, string signIn)
    {
        using var _ = new UICulture(culture);
        var tenant = new TenantSnapshot("a", "Customer A", true, new PresenceInfo("Busy", "InACall"), TenantRole.Source,
            TenantHealth.Ok, null, null, null, null);

        Assert.Equal(source, tenant.StatusLine);
        Assert.Equal(synced, (tenant with { Role = TenantRole.Synced, Presence = new PresenceInfo("Busy", "Busy") }).StatusLine);
        Assert.Equal(signIn, (tenant with { Health = TenantHealth.AuthRequired }).StatusLine);
    }
}
