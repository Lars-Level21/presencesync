# PresenceSync – Information for customer tenant admins

For IT administrators of a tenant in which a user – usually an external consultant with a member account – wants to
use PresenceSync and needs your consent. This page explains what you are consenting to, how to restrict consent to
this one user and how to monitor and revoke it.

> **Summary:** PresenceSync is a local Windows app without a server. Via Microsoft Graph it reads and sets only the
> **signed-in user's own Teams status**. It requests only the delegated permissions `User.Read`,
> `Presence.ReadWrite` and `offline_access`. The only thing transferred between tenants is the signal
> "busy / do not disturb" – no content, no names, no appointments. Consent can be restricted to a single user and
> revoked at any time.

## What this is about

External staff are often signed in to several tenants at once in the Teams client, but the Teams status is separate
per tenant. When the employee is on a call in their employer's tenant, they still appear as "Available" in your
tenant. In that case PresenceSync sets them to "Busy" in your tenant as well and resets it afterwards.
**For your users, the external colleague's status becomes more reliable.**

How it works at a glance: [README](../README.md). Technical details and data flows:
[architecture.md](architecture.md).

## The application

| | |
| --- | --- |
| Name | PresenceSync |
| Publisher | shown in the consent dialog; with a blue check mark if the publisher is verified |
| Client ID | `<clientId>` – from the requesting user or their organization |
| App type | public client (Windows desktop app), multi-tenant |
| Secrets / certificates | none |
| Application permissions (without a user) | **none** |
| Delegated permissions | `User.Read`, `Presence.ReadWrite`, `offline_access` |
| Source code | public, MIT license: <https://github.com/Lars-Level21/presencesync> |

Consent creates an **enterprise application** "PresenceSync" in your tenant. The app registration itself lives in the
publisher's tenant – usually the organization of the requesting user.

## Permissions

| Permission | Type | Admin consent per Microsoft | What PresenceSync uses it for |
| --- | --- | --- | --- |
| `User.Read` | delegated | no | sign-in, own object ID, once the name of your tenant as a label in the app |
| `Presence.ReadWrite` | delegated | no | read and set own Teams status |
| `offline_access` | delegated | no | token renewal in the background |

If your tenant still asks for admin approval, that is due to your consent policy, not the kind of permissions. The
complete list of Graph calls with their frequency is in
[architecture.md → Graph calls](architecture.md#graph-calls).

**What the app cannot and does not do**

- Read or set the status of **other** users – delegated permissions apply only to the signed-in user, and the app
  addresses only that user's own ID.
- No access to chats, messages, mail, files, calendars, contacts, Teams content or other people's directory data.
- No access without a signed-in user, no server, no telemetry.

## Data flow in brief

On the user's device, PresenceSync reads the user's status in each of their tenants. If they are busy in one tenant,
the app sets its own status session "Busy" or "Do not disturb" in the others, which expires after 5 minutes and is
renewed continuously as long as the reason persists.

- **Out of your tenant** goes only the information "busy / not busy" – no call partners, subjects, participants,
  times or content.
- **Transfer** happens only via the user's device, not via third parties.
- **Stored** on the device are only tenant ID, display name and sign-in name. The token cache is bound to the user via
  Windows DPAPI, the log contains no tokens, and sign-in names are masked.

Sequence diagram and table of all data: [architecture.md → Data flow](architecture.md#data-flow-during-auto-sync).

## Security

- **Least privilege:** three delegated low-privilege permissions, no application permissions, no secrets.
- **Conditional Access applies in full.** The app is a regular public client and is subject to all policies of your
  tenant (MFA, device compliance, location, session lifetime). If a policy requires a device the user does not have,
  the app cannot be used in your tenant. It does not try to circumvent that: no third-party client IDs, no
  undocumented endpoints.
- **No automatic sign-in dialogs:** interactive sign-in only after an explicit click by the user.
- **Fail-safe:** status sessions expire on their own after 5 minutes; they are actively cleared when the app exits
  and when the user signs out of Windows.
- **User decision takes precedence:** a status set in the Teams client is not overwritten.
- **Low load:** about 3 reads per minute per tenant, writes only on status changes; throttling is honored with
  `Retry-After` and backoff.
- **Transparent:** open source, automated tests, vulnerability scanning in the build pipeline; security model and
  known limitations in [SECURITY.md](../SECURITY.md).

**Worst case:** if an attacker gains code execution on the user's device and abuses this app, they can read and
change the Teams status of **this one user** – nothing more.

## Granting consent

Replace `<clientId>` with the client ID from the request and `<tenant>` with your tenant ID or domain.

### Option 1 (recommended): for this one user only

The permissions apply exclusively to the requesting user's account; nothing is granted for anyone else.

```powershell
Connect-MgGraph -Scopes "Application.ReadWrite.All","DelegatedPermissionGrant.ReadWrite.All" -TenantId <tenant>

$app   = Get-MgServicePrincipal -Filter "appId eq '<clientId>'"
if (-not $app) { $app = New-MgServicePrincipal -AppId '<clientId>' }
$graph = Get-MgServicePrincipal -Filter "appId eq '00000003-0000-0000-c000-000000000000'"
$user  = Get-MgUser -UserId '<user-upn>'

New-MgOauth2PermissionGrant -BodyParameter @{
    clientId    = $app.Id
    consentType = 'Principal'
    principalId = $user.Id
    resourceId  = $graph.Id
    scope       = 'User.Read Presence.ReadWrite offline_access'
}
```

### Option 2: admin consent with required assignment

1. Grant consent: `https://login.microsoftonline.com/<tenant>/adminconsent?client_id=<clientId>`
2. *Entra admin center → Enterprise applications → PresenceSync → Properties →* **Assignment required? = Yes**
3. *Users and groups →* assign only the requesting user.

### Option 3: via the admin consent workflow

If the user has already submitted a request, you will find it under *Enterprise applications → Admin consent
requests*. After approval, we recommend also performing steps 2 and 3 from option 2.

## Monitoring and revoking

| Task | Where |
| --- | --- |
| See the app's sign-ins | *Entra ID → Sign-in logs*, filter Application = "PresenceSync" |
| Trace Graph calls | Microsoft Graph activity logs (if enabled), filter on the client ID |
| Review granted permissions | *Enterprise applications → PresenceSync → Permissions* |
| Revoke access for one user | remove the assignment or delete their `oauth2PermissionGrant` |
| Block the app completely | *Enterprise applications → PresenceSync → Properties →* "Enabled for users to sign in?" = No, or delete the enterprise application |
| End active sessions | *Users → Revoke sessions* |

After a revocation, PresenceSync shows the tenant as "Blocked: …" or "Sign-in required"; any status session that was
set expires after 5 minutes at most.

## FAQ

**Does PresenceSync see who the user is on a call with in our tenant?**
No. For the user's own status, Graph only returns availability and activity (e.g. "In a call"), no call partners.

**Does another customer learn anything about our tenant?**
Only that the user is currently "busy" – the same as if they had set "Busy" manually there.

**Can the app change the status of our employees?**
No. Delegated permissions apply only to the signed-in user; the app addresses only that user's ID.

**Does the app need administrator rights on the device?**
No. It runs in the user context.

**Will the app request more permissions in the future?**
An optional calendar sync is planned that creates anonymous "blocker" appointments in the user's **own** calendar
(`Calendars.ReadWrite`). This permission is not part of the current registration and would only be requested
**separately** when the feature is enabled, with its own consent and updated documentation.

## Contact

Please direct questions about a specific request to the requesting user or their organization.
Security reports about the software: [SECURITY.md](../SECURITY.md).
