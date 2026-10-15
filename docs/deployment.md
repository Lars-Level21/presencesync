# Deploying PresenceSync

For IT departments that provide PresenceSync to their employees – and for individual users, who only need section 1.
What the app does and how it is built: [README](../README.md), [architecture.md](architecture.md).

**Process**

1. [Create the app registration](#1-app-registration) in the organization's tenant and verify the publisher – once.
2. [Set policies](#2-policies): centrally set the client ID and optionally the home tenant.
3. [Distribute the app](#3-distribute-the-app), e.g. as an Intune Win32 app.
4. [Connect customer tenants](#4-connect-customer-tenants): employees add their customer tenants; where required,
   the customer's IT grants consent.

## 1. App registration

PresenceSync needs exactly **one** app registration in the organization's tenant. It is multi-tenant; in each
customer tenant an enterprise application (service principal) is created automatically on first sign-in or on admin
consent. Employees do not create their own app registrations.

### Key settings

| Setting | Value |
| --- | --- |
| Supported account types | Accounts in any organizational directory (`AzureADMultipleOrgs`) |
| Platform | Mobile and desktop applications |
| Redirect URIs | `ms-appx-web://microsoft.aad.brokerplugin/{clientId}` (Windows sign-in) and `http://localhost` (browser fallback) |
| Allow public client flows | Yes |
| Secrets / certificates | none |
| API permissions (Microsoft Graph, delegated) | `User.Read`, `Presence.ReadWrite`, `offline_access` |

### Create via script (Azure CLI)

```powershell
az login --tenant <tenant-id> --allow-no-subscriptions
./scripts/New-AppRegistration.ps1 -DisplayName "PresenceSync" -MpnId <PartnerGlobal-ID>
```

The script creates the registration, sets both redirect URIs, adds the three delegated permissions (it reads their
IDs from Microsoft Graph at runtime), creates the service principal in your own tenant and optionally sets the
verified publisher. The `appId` it outputs is the **client ID**.

### Create in the portal

1. Entra admin center → *Applications* → *App registrations* → *New registration*
   - Name `PresenceSync`, account type *Accounts in any organizational directory*
   - Redirect URI: platform *Public client/native (mobile & desktop)*, `http://localhost`
2. *Authentication* → *Add a platform* → *Mobile and desktop applications* →
   add `ms-appx-web://microsoft.aad.brokerplugin/<clientId>`; *Allow public client flows* = **Yes**.
3. *API permissions* → Microsoft Graph → *Delegated*: `User.Read`, `Presence.ReadWrite`, `offline_access`.
4. *Branding & properties*: optionally add a publisher domain and logo – customer admins see this information in the
   consent dialog.
5. Verify the publisher (next section).

### Publisher verification

**Why:** For newly registered multi-tenant apps **without** a verified publisher, Microsoft largely blocks user
consent; many tenants also only allow it for verified publishers. Without verification, employees in customer tenants
see "Need admin approval" (`AADSTS90094`). With verification, the consent dialog shows the publisher with a blue
check mark, and users can usually consent to PresenceSync's low-privilege permissions themselves – unless the
customer's IT has turned that off entirely. Verification is set once and applies to all customer tenants.

**Prerequisites**

| What | Where to check |
| --- | --- |
| Partner account in the Microsoft AI Cloud Partner Program (formerly MPN), verification completed ("authorized"/"success") | [Partner Center → Partner profile](https://partner.microsoft.com/pcv/accountsettings/connectedpartnerprofile) |
| **Partner ID of type `PartnerGlobal`** – the location ID (*PartnerLocation*) looks almost the same and is rejected | Partner Center → Account settings → *Identifiers* → *Microsoft Cloud Partners Program* → column *Type* |
| The domain of the primary contact e-mail in Partner Center is DNS-verified as a custom domain in the tenant | Entra admin center → *Domain names* |
| Entra role *Application Administrator* or *Cloud Application Administrator* | Entra admin center → *Roles & admins* |
| Partner Center role *Global Admin*, *AI Cloud Partner Program Admin* or *Accounts Admin* for the same user | [Partner Center → User management](https://partner.microsoft.com/pcv/users) |
| Sign-in **with MFA in the same session** | if in doubt, sign out and sign in again |

**Steps**

1. [Entra admin center](https://entra.microsoft.com) → *App registrations* → **PresenceSync** → **Branding & properties**.
2. **Publisher domain**: if it shows `*.onmicrosoft.com`, select your own verified domain via *Update domain*.
3. **Publisher verification** → **Add MPN ID to verify publisher** → enter the **PartnerGlobal** ID in the panel →
   **Verify and save**.
4. *Branding & properties* then shows the MPN ID and the **Publisher display name** with a blue check mark. The name
   comes from the Partner Center profile and should be the company name; change it only there, then *Check for updates*.

With the script, `-MpnId <PartnerGlobal-ID>` performs step 3; the publisher domain (step 2) must be set beforehand.

**Troubleshooting:** For many validation errors the portal only shows *"There's a temporary problem with the
service"*. The same call made directly against Graph returns the real error code (object ID of the app, not the
client ID):

```powershell
$objectId = az ad app show --id <clientId> --query id -o tsv
'{"verifiedPublisherId":"<PartnerGlobal-ID>"}' | Set-Content $env:TEMP\pv.json
az rest --method POST --uri "https://graph.microsoft.com/v1.0/applications/$objectId/setVerifiedPublisher" --headers "Content-Type=application/json" --body "@$env:TEMP\pv.json"
```

| Error code | Cause | Fix |
| --- | --- | --- |
| `MPNGlobalAccountNotFound`, `MPNAccountInvalid`, `NoPublisherIdOnAssociatedMPNAccount` | wrong ID, usually the location ID | use the ID of type `PartnerGlobal` |
| `MPNAccountNotFoundOrNoAccess` | Partner Center role missing or tenant not linked to the partner account | check *User management* or *Tenant management* |
| `MPNAccountNotVetted` | partner verification not completed | complete the open actions in the partner profile |
| `NoPublisherDomainOnApplication`, `PublisherDomainMismatch` | publisher domain missing or does not match the primary contact domain | step 2; verify the domain in the tenant |
| `NotAuthorizedToVerifyPublisher` | Entra role missing | have *Application Administrator* assigned |
| `InteractionRequired` | no MFA in the session | sign in again with MFA |
| `UnableToAddPublisher`, `UserUnableToAddPublisher` | Microsoft risk assessment | check user risk or try again later |

Full list: [Troubleshoot publisher verification](https://learn.microsoft.com/entra/identity-platform/troubleshoot-publisher-verification).

## 2. Policies

With policies the organization sets the client ID; employees cannot change it in the app.

Registry key `SOFTWARE\Policies\PresenceSync` under `HKLM` (computer) or `HKCU` (user). For each value, the computer
policy takes precedence over the user policy; both take precedence over `config.json`.

| Value | Type | Effect |
| --- | --- | --- |
| `ClientId` | REG_SZ (GUID) | Client ID of the app registration. Used and locked in the settings ("Set by your organization"). |
| `AllowCustomClientId` | REG_DWORD 0/1 | 1 = `ClientId` is only a default for an empty setting. Default 0. |
| `HomeTenantId` | REG_SZ (GUID) | The home tenant is added automatically, marked as *Home* and cannot be removed. If the Windows account is a member account of this tenant, the app signs in **without user interaction**; otherwise a click on *Sign in …* is enough. |

- Invalid values (not a GUID) are ignored and noted in the log.
- `SOFTWARE\Policies` is protected by Windows in both hives: standard users can neither create nor change the values.
- The app reads the policy at startup and before every save of the settings. While a policy is active, the settings
  show "Managed by your organization".
- Automatic sign-in only accepts a **member account of exactly this tenant**, not a guest identity of the Windows
  account in another tenant.

### Intune with imported ADMX template (recommended)

1. *Intune admin center → Devices → Configuration → Import ADMX*: upload `deploy/admx/PresenceSync.admx` and
   `deploy/admx/en-US/PresenceSync.adml`.
2. *Create policy → Windows 10 and later → Templates → Imported Administrative templates*.
3. Under *Computer Configuration → PresenceSync*, enable **Set the app registration client ID** and optionally
   **Set the home tenant**.
4. Assign it to the employees' device or user group.

### Intune platform script

*Devices → Scripts and remediations → Platform scripts*: [`scripts/Set-PresenceSyncPolicy.ps1`](../scripts/Set-PresenceSyncPolicy.ps1)
with fixed parameters, *Run this script using the logged on credentials* = **No** (SYSTEM),
*Run script in 64 bit PowerShell Host* = **Yes**.

Manually as administrator:

```powershell
.\scripts\Set-PresenceSyncPolicy.ps1 -ClientId <client-id> -HomeTenantId <tenant-id>
```

Remove: `.\scripts\Set-PresenceSyncPolicy.ps1 -Remove`

### Group Policy (Active Directory)

1. Copy `deploy/admx/PresenceSync.admx` to `\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\`, and
   `de-DE\PresenceSync.adml` and `en-US\PresenceSync.adml` into the subfolders of the same name.
2. Group Policy Management → *Computer (or User) Configuration → Policies → Administrative Templates →
   PresenceSync*.

## 3. Distribute the app

### Intune Win32 app

Package folder:

```
PresenceSync-Package\
  PresenceSync.exe              from the release, verify the checksum (SECURITY.md)
  Install-PresenceSync.ps1      deploy/intune
  Uninstall-PresenceSync.ps1    deploy/intune
  Set-PresenceSyncPolicy.ps1    scripts – only if the policy should be installed as well
```

Package it into an `.intunewin` with the *Microsoft Win32 Content Prep Tool* and create the app in Intune:

| Setting | Value |
| --- | --- |
| Install command | `powershell.exe -ExecutionPolicy Bypass -NoProfile -File .\Install-PresenceSync.ps1` (optional `-ClientId <guid> -HomeTenantId <guid>`) |
| Uninstall command | `powershell.exe -ExecutionPolicy Bypass -NoProfile -File .\Uninstall-PresenceSync.ps1` |
| Install behavior | System |
| Detection rule | File `%ProgramFiles%\PresenceSync\PresenceSync.exe` exists |
| Requirements | Windows 10 1809+ or Windows 11, x64 |

The install script places the app in `%ProgramFiles%\PresenceSync` and registers autostart for all users. It is
better to manage the policy separately via ADMX – then it can be changed without redistributing the app.

> The release exe is deliberately not signed ([SECURITY.md](../SECURITY.md#known-limitations)). With AppLocker/WDAC,
> either allow it by hash or build it from source and sign it with your own certificate. SmartScreen warnings usually
> do not occur when distributing via Intune, because the file is not downloaded from the internet.

### What employees experience afterwards

1. PresenceSync starts in the notification area after Windows sign-in.
2. With `HomeTenantId`, the home tenant is set up and usually already signed in.
3. Employees add customer tenants themselves (next section).

## 4. Connect customer tenants

1. *Tray icon → gear icon → Add tenant → Choose account …*, pick the account of the customer tenant, **Save**.
   Guest accounts (B2B): first enter the customer's tenant ID or domain under *Advanced: guest account or specific
   tenant*.
2. If the customer tenant allows user consent, the employee confirms the consent dialog themselves.
3. Otherwise "Need admin approval" appears. In that case contact the customer's IT with the
   [request template](consent-request-template.md) and [consent.md](consent.md) – with **your** client ID and
   publisher name filled in. Once approved, add the tenant again.

## 5. Verification and troubleshooting

- Policy: `Get-ItemProperty HKLM:\SOFTWARE\Policies\PresenceSync`
- App: settings show "Managed by your organization", the client ID is grayed out.
- Log (`%LOCALAPPDATA%\PresenceSync\logs`, tray window → document icon): `Policy active: …`,
  `Configuration active: N tenant(s)`, per tenant `<tenant>: connected`; with automatic sign-in
  `Signed in automatically with Windows account`.

If a tenant shows "Sign-in required" or "Blocked: …", the app states the reason in plain language:

| Code | Meaning | Solution |
| --- | --- | --- |
| `AADSTS65001` | consent missing | *Sign in …* and consent |
| `AADSTS90094` | admin approval required | customer's IT, see section 4 |
| `AADSTS50105` | assignment required, user not assigned | customer's IT assigns the user to the enterprise application |
| `AADSTS50076` | MFA required | *Sign in …* |
| `AADSTS53000` / `53001` | Conditional Access requires a compliant or joined device | cannot be used with this device |
| HTTP 403 | no Teams/presence access (license) or consent revoked | check license or consent |
