using PresenceSync.Core.Configuration;
using PresenceSync.Core.Diagnostics;

namespace PresenceSync.Core.Tests;

public sealed class SecurityTests
{
    [Fact]
    public void Redaction_masks_email_addresses()
    {
        var text = LogRedaction.Redact("AADSTS50076: user lars.martin@customer-a.de must use MFA");
        Assert.DoesNotContain("lars.martin@customer-a.de", text);
        Assert.Contains("la***@cu***.de", text);
    }

    [Fact]
    public void Redaction_removes_jwt_and_bearer_tokens()
    {
        const string jwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJhdWQiOiJodHRwczovL2dyYXBoIn0.c2lnbmF0dXJl";
        var text = LogRedaction.Redact($"token {jwt} header Authorization: Bearer abc.def-ghi");
        Assert.DoesNotContain("eyJ0eXAi", text);
        Assert.DoesNotContain("abc.def-ghi", text);
        Assert.Contains("[TOKEN]", text);
    }

    [Fact]
    public void Redaction_leaves_ordinary_text_and_masked_upns_alone()
    {
        const string text = "LEVEL21: verbunden, Anmeldung erfolgreich: la***@cr***.com in Tenant 6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b";
        Assert.Equal(text, LogRedaction.Redact(text));
    }

    [Theory]
    [InlineData("6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b", true)]
    [InlineData("level21.info", true)]
    [InlineData("contoso.onmicrosoft.com", true)]
    [InlineData("customer-a.co.uk", true)]
    [InlineData("", false)]
    [InlineData("contoso", false)]
    [InlineData("../common", false)]
    [InlineData("contoso.com/oauth2", false)]
    [InlineData("organizations", false)]
    public void Tenant_input_accepts_only_guid_or_domain(string value, bool expected) =>
        Assert.Equal(expected, TenantInput.IsValid(value));
}
