using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using PresenceSync.Core.Diagnostics;
using PresenceSync.Core.Sync;

namespace PresenceSync.Graph;

/// <summary>
/// One MSAL public client per tenant (authority login.microsoftonline.com/{tenant}), WAM broker enabled so the
/// accounts already signed in to Windows/Teams are used via SSO. Silent calls never show UI; interactive sign-in
/// only happens when the user triggers it from the tray.
/// </summary>
public sealed class TenantAuthenticator
{
    public static readonly string[] PresenceScopes =
    [
        "https://graph.microsoft.com/User.Read",
        "https://graph.microsoft.com/Presence.ReadWrite",
    ];

    private readonly IPublicClientApplication _app;
    private readonly ILogger _log;
    private readonly bool _useBroker;

    /// <summary>Authority used once, to let the user pick a work/school account; the token lands in the account's own tenant.</summary>
    private const string AccountSelectionAuthority = "organizations";

    public TenantAuthenticator(string clientId, string tenant, string? upn, bool useBroker, MsalCacheHelper? cache, ILogger log)
        : this(clientId, ValidateTenant(tenant), upn, useBroker, cache, log, validated: true)
    {
    }

    /// <summary>
    /// For adding a tenant without knowing it: shows the Windows account picker and signs in against the selected
    /// member account's own tenant (<see cref="AuthenticationResult.TenantId"/>). Not suitable for guest accounts –
    /// they would land in their home tenant. The app then continues with a tenant-specific authenticator.
    /// </summary>
    public static TenantAuthenticator ForAccountSelection(string clientId, bool useBroker, MsalCacheHelper? cache, ILogger log) =>
        new(clientId, AccountSelectionAuthority, null, useBroker, cache, log, validated: true);

    private TenantAuthenticator(string clientId, string tenant, string? upn, bool useBroker, MsalCacheHelper? cache, ILogger log, bool validated)
    {
        _ = validated;
        _useBroker = useBroker;
        Tenant = tenant;
        Upn = string.IsNullOrWhiteSpace(upn) ? null : upn;
        _log = log;

        var builder = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenant)
            // Used only if WAM is unavailable (system browser fallback); WAM uses ms-appx-web://microsoft.aad.brokerplugin/{clientId}.
            .WithDefaultRedirectUri()
            .WithClientName("PresenceSync");

        if (useBroker)
            builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "PresenceSync" });

        _app = builder.Build();
        cache?.RegisterCache(_app.UserTokenCache);
    }

    // Only a tenant ID or domain may end up in the authority URL (no "common", "organizations" or path segments).
    private static string ValidateTenant(string tenant) =>
        PresenceSync.Core.Configuration.TenantInput.IsValid(tenant)
            ? tenant.Trim()
            : throw new ArgumentException("Ungültige Tenant-ID oder Domain.", nameof(tenant));

    /// <summary>Tenant ID (GUID) or domain the authority was built with.</summary>
    public string Tenant { get; }

    public string? Upn { get; private set; }

    /// <summary>
    /// Allows a silent first sign-in with the account signed in to Windows (WAM) when no UPN is known yet.
    /// Only set for the home tenant pre-configured by organisation policy.
    /// </summary>
    public bool AllowOperatingSystemAccount { get; set; }

    /// <summary>Silent token acquisition. Throws <see cref="TenantAccessException"/>, never shows UI.</summary>
    public async Task<string> GetTokenSilentAsync(bool forceRefresh, CancellationToken ct)
    {
        try
        {
            var account = await FindAccountAsync();
            AcquireTokenSilentParameterBuilder request;
            if (account is not null)
                request = _app.AcquireTokenSilent(PresenceScopes, account);
            else if (Upn is not null)
                request = _app.AcquireTokenSilent(PresenceScopes, Upn);
            else if (AllowOperatingSystemAccount && _useBroker)
                request = _app.AcquireTokenSilent(PresenceScopes, PublicClientApplication.OperatingSystemAccount);
            else
                throw new TenantAccessException(TenantFailureKind.AuthRequired, "Noch nicht angemeldet");

            var result = await request.WithForceRefresh(forceRefresh).ExecuteAsync(ct);
            if (Upn is null)
            {
                // Zero-touch sign-in with the Windows account: only accept a member account of exactly this tenant,
                // never a guest identity of the Windows account in a foreign tenant.
                var home = result.Account.HomeAccountId?.TenantId;
                if (home is null || !string.Equals(home, result.TenantId, StringComparison.OrdinalIgnoreCase))
                    throw new TenantAccessException(TenantFailureKind.AuthRequired, "Windows-Konto gehört nicht zu diesem Tenant – bitte anmelden");
                Upn = result.Account.Username;
                _log.LogInformation("Automatisch mit Windows-Konto angemeldet: {Upn} in Tenant {TenantId}", Mask.Upn(Upn), result.TenantId);
            }
            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            throw Map(ex);
        }
    }

    /// <summary>Interactive sign-in, parented to <paramref name="parentWindow"/>. Only call on explicit user action.</summary>
    public async Task<AuthenticationResult> SignInInteractiveAsync(IntPtr parentWindow, CancellationToken ct)
    {
        try
        {
            var request = _app.AcquireTokenInteractive(PresenceScopes).WithParentActivityOrWindow(parentWindow);
            request = Upn is not null ? request.WithLoginHint(Upn) : request.WithPrompt(Prompt.SelectAccount);

            var result = await request.ExecuteAsync(ct);
            Upn = result.Account.Username;
            _log.LogInformation("Anmeldung erfolgreich: {Upn} in Tenant {TenantId}", Mask.Upn(Upn), result.TenantId);
            return result;
        }
        catch (MsalException ex)
        {
            throw Map(ex);
        }
    }

    private async Task<IAccount?> FindAccountAsync()
    {
        if (Upn is null)
            return null;
        var accounts = await _app.GetAccountsAsync();
        return accounts.FirstOrDefault(a => string.Equals(a.Username, Upn, StringComparison.OrdinalIgnoreCase));
    }

    internal static TenantAccessException Map(MsalException ex)
    {
        if (ex is MsalClientException { ErrorCode: MsalError.AuthenticationCanceledError })
            return new TenantAccessException(TenantFailureKind.AuthRequired, "Anmeldung abgebrochen", inner: ex);

        if (AadErrorCatalog.Classify(ex.Message) is { } known)
            return new TenantAccessException(known.Kind, known.Text, inner: ex);

        return ex switch
        {
            MsalUiRequiredException ui => new TenantAccessException(TenantFailureKind.AuthRequired,
                $"Anmeldung erforderlich ({ui.ErrorCode})", inner: ex),
            MsalServiceException { StatusCode: 429 } svc => new TenantAccessException(TenantFailureKind.Throttled,
                "Anmeldedienst drosselt Anfragen", svc.Headers?.RetryAfter?.Delta, ex),
            MsalServiceException { StatusCode: >= 500 } => new TenantAccessException(TenantFailureKind.Transient,
                "Anmeldedienst nicht erreichbar", inner: ex),
            MsalServiceException svc => new TenantAccessException(TenantFailureKind.AuthRequired,
                $"Anmeldung fehlgeschlagen ({svc.ErrorCode})", inner: ex),
            _ => new TenantAccessException(TenantFailureKind.Transient, $"Anmeldung nicht möglich ({ex.ErrorCode})", inner: ex),
        };
    }
}
