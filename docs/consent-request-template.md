# Template: request to a customer tenant's IT

*For employees whose customer tenant requires admin approval. Copy the text, replace the placeholders in angle
brackets and send it as an e-mail. Background for the admins: [consent.md](consent.md).*

---

**Subject:** Consent for the "PresenceSync" app (Teams status only, my account only)

Hi,

I work in your tenant with the account `<upn in customer tenant>` and am signed in to Microsoft Teams in other tenants
at the same time (`<own organization>` and other customers). So that you can see when I'm in a call or meeting, even if
it takes place in another tenant, I use the PresenceSync app. It only syncs **my own** Teams status between my
accounts.

Your tenant policy requires admin approval for this:

| | |
| --- | --- |
| App name | PresenceSync |
| Client ID | `<clientId>` |
| Publisher | `<publisher, as shown in the consent dialog>` (verified publisher) |
| App type | Windows desktop app, no server, no secrets |
| Permissions | delegated only: `User.Read`, `Presence.ReadWrite`, `offline_access` |

**What the app does:** It reads my own Teams status and sets it to "Busy" while I'm in a call in another tenant. To do
this, it creates its own status session, which expires on its own after 5 minutes at most.

**What the app doesn't do:** no access to other users' data or status, chats, mail, files or calendars; no
application permissions, no backend, no telemetry. The only thing leaving your tenant is the signal
"busy / not busy".

**The easiest option for you:** grant consent **for my account only** (`consentType = Principal`), or grant admin
consent and restrict the enterprise application to my account using "Assignment required".
Instructions with a PowerShell snippet, permission details, data flows and how to revoke:

- Information for admins: https://github.com/Lars-Level21/presencesync/blob/main/docs/consent.md
- Security: https://github.com/Lars-Level21/presencesync/blob/main/SECURITY.md

Admin consent for the whole tenant (please restrict it to my account afterwards):

```
https://login.microsoftonline.com/<customer-tenant-id>/adminconsent?client_id=<clientId>
```

Thanks a lot!
`<Name>`
