# PresenceSync – Specification

> Original requirements the project started with (historical; file names and structure have changed since). Current documentation: [README](../README.md). Deviations and decisions are listed in the appendix.

Windows tray app that syncs the Microsoft Teams status across multiple Entra ID tenants and makes it controllable manually.

## 1. Background

- One home tenant and three customer tenants, with a separate member account (not a guest) in each customer tenant.
- In the new Teams client, all accounts are signed in in parallel.
- Presence is isolated per tenant. A call in tenant A only sets A to "Busy"; all others stay "Available".
- Adjusting the status manually requires switching organizations in the Teams client every time. That is impractical in day-to-day project work.

## 2. Goals

1. **Auto-sync:** When I am in a call, in a meeting, presenting or on DnD in one tenant, I automatically appear as busy in all other tenants. When that state ends, the status reverts on its own.
2. **Manual control:** Set the status via the tray menu for all tenants at once or per tenant, without the Teams client.
3. **Phase 2 – calendar blockers:** Appointments from each tenant are mirrored as anonymous busy blockers into the calendars of the other tenants (free/busy + calendar-based presence).

## 3. Non-goals

- No bypassing consent via first-party client IDs (Teams client ID) or undocumented endpoints. Official Microsoft Graph APIs only.
- No sharing of content (subject, participants, location) between customer tenants.
- No server/cloud backend. Everything runs locally on the client.
- No chat or messaging features.

## 4. Tech stack

- .NET 8, WinForms or WPF with `NotifyIcon` (no main window needed, only a settings dialog)
- `Microsoft.Identity.Client` + `Microsoft.Identity.Client.Broker` (WAM)
- `Microsoft.Graph` SDK v5 or a lean custom `HttpClient` – justify the decision in the code
- Logging: Serilog, rolling file under `%LOCALAPPDATA%\PresenceSync\logs`
- Configuration: `%APPDATA%\PresenceSync\config.json`
- Delivery: self-contained single-file `win-x64`, autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (can be turned off in the settings)

## 5. App registration (home tenant)

- Multi-tenant (`AzureADMultipleOrgs`), public client, no secrets
- Redirect URI for WAM: `ms-appx-web://microsoft.aad.brokerplugin/{clientId}`, plus `http://localhost` as fallback
- Delegated permissions:
  - `User.Read`
  - `Presence.ReadWrite` (phase 1)
  - `Calendars.ReadWrite` (phase 2, only request when phase 2 is enabled – incremental consent)
  - `offline_access`
- Publisher verification via the Partner Center ID (MPN), so that user consent is possible at all in tenants with the "verified publishers" policy
- A service principal is created in each customer tenant on first sign-in. For the admin consent case, include a short justification for the customer's IT as `docs/consent-request.md` (scope, delegated only, no data access, can be restricted to my user via "Assignment required")

## 6. Authentication

- One `IPublicClientApplication` per tenant with authority `https://login.microsoftonline.com/{tenantId}`
- WAM broker enabled (`WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))`) so the existing Windows/Teams accounts work via SSO
- Token acquisition: `AcquireTokenSilent` with the stored account (login hint = UPN from the config), fallback `AcquireTokenInteractive` with parent window handle
- Never let interactive re-auth pop up automatically during a call. Instead, set the tray icon to "warning" and mark the tenant in the menu as "Sign-in required"
- Properly detect Conditional Access errors (e.g. `AADSTS53000` compliant device, `AADSTS50076` MFA) and show them in plain language in the menu/log. The tenant then continues in the "disabled" state without blocking the others

## 7. Configuration

```json
{
  "clientId": "<guid>",
  "pollIntervalSeconds": 20,
  "sessionExpirationMinutes": 5,
  "autoSync": true,
  "tenants": [
    { "name": "LEVEL21", "tenantId": "<guid>", "upn": "lars@…", "enabled": true, "isHome": true },
    { "name": "Customer A", "tenantId": "<guid>", "upn": "lars.martin@customer-a.…", "enabled": true }
  ],
  "calendarMirror": { "enabled": false, "daysAhead": 14, "intervalMinutes": 10 }
}
```

Tenants are added via the settings dialog: enter the tenant ID or domain, sign in, take over the UPN.

## 8. Phase 1 – presence sync

### 8.1 Graph endpoints

- Read: `GET /me/presence` → `availability`, `activity`
- Automatic sync: `POST /users/{userId}/presence/setPresence`

  ```json
  { "sessionId": "<clientId>", "availability": "Busy", "activity": "InACall", "expirationDuration": "PT5M" }
  ```

  Check the allowed combinations against the docs (Available/Available, Busy/InACall, Busy/InAConferenceCall, Away/Away, DoNotDisturb/Presenting). `expirationDuration` min. 5, max. 240 minutes.
- End session: `POST /users/{userId}/presence/clearPresence` with `sessionId`
- Manual control: `POST /users/{userId}/presence/setUserPreferredPresence` or `clearUserPreferredPresence`

`setPresence` creates its own app session, which Teams aggregates with the other sessions (desktop, mobile). This means nothing is hard-overwritten, a manually set DnD stays in place, and if the app crashes the status expires on its own.

### 8.2 Sync logic

Polling loop, every 20 s by default, in parallel across all active tenants:

1. Read the presence of all tenants.
2. Determine source tenants: tenants whose status is "busy" (`activity` ∈ {`InACall`, `InAConferenceCall`, `InAMeeting`, `Presenting`, `UrgentInterruptionsOnly`} or `availability` = `DoNotDisturb`) and in which the app has not currently set its own session.
3. If there is at least one source tenant:
   - In all other active tenants, `setPresence` Busy/InACall (or DoNotDisturb/Presenting if the source is DnD/Presenting) with an expiration of 5 min.
   - The session is renewed on every poll as long as the source is busy (refresh only when the remaining time is < 2 min, to save API calls).
4. If there is no source tenant any more: call `clearPresence` in all tenants with an own session (do not just let it expire, so the status is correct immediately).

Avoid feedback loops: a tenant in which the app itself has set a session must never be treated as a source. The state "own session active, since, until" is kept in memory per tenant. Known limitation: if a real call starts in such a tenant at the same time, it is not recognized as a new source. This is acceptable for the use case; document it in the code as a known limitation.

When the app exits and on Windows logoff/shutdown (`SessionEnding`), try `clearPresence` for all own sessions (best effort with a short timeout).

### 8.3 Error handling

- HTTP 429 / 503: respect `Retry-After`, exponential backoff per tenant
- An error in one tenant must not block the loop for the others
- Network loss: pause the loop, restart after reconnecting
- Standby/resume (`SystemEvents.PowerModeChanged`): trigger a poll immediately after resume

## 9. Tray UI

- The icon shows the aggregated state: green (all available), red (busy somewhere / sync active), gray (auto-sync off), yellow (at least one tenant with an auth problem)
- Tooltip: short overview per tenant, e.g. `Customer A: In a call (source)`, `Customer B: Busy (sync)`
- Context menu:
  - All tenants: Available · Busy · Do not disturb · Be right back · Away · Reset
  - For manual statuses, a duration can be chosen in each case: 30 min · 1 h · 2 h · Until end of day · Unlimited
  - Per tenant, a submenu with the same options and the current status
  - Auto-sync on/off
  - Settings …
  - Open log
  - Exit
- Optional: global hotkey for "All → Do not disturb 1 h" and "All → Reset" (configurable)

Manual statuses use `setUserPreferredPresence` and take precedence over auto-sync. As long as a manual status is active, auto-sync sets nothing in that tenant.

## 10. Phase 2 – calendar blockers

- Per tenant `GET /me/calendarView?startDateTime=…&endDateTime=…` (time range according to `daysAhead`)
- For every appointment with `showAs` ∈ {`busy`, `oof`, `tentative`}, create a blocker in every other tenant:
  - Subject: configurable, default `Blocker` – no content from the source tenant (subject, participants, location, body, Teams link)
  - `showAs` as in the source, `sensitivity: private`, `isReminderOn: false`, no attendees, no online meeting info
  - Mapping via a `singleValueExtendedProperty` (custom GUID-based property with source tenant ID + hash of the source event ID)
- Never mirror blockers again: events with the custom extended property are ignored when reading
- Reconciliation: create new blockers, update changed times, remove blockers for deleted/cancelled source appointments
- Handle all-day appointments and series correctly (calendarView returns expanded instances)
- Time zones: header `Prefer: outlook.timezone="UTC"` when reading, write blockers in UTC
- Dry-run mode that only logs what would be created/changed/deleted

## 11. Security and privacy

- No secrets, no tokens in plain text. Token cache exclusively via WAM/MSAL
- Logs contain no tokens, no appointment content and no personal data of third parties. UPNs only masked
- Least privilege: only request the phase 2 scope when phase 2 is enabled
- No telemetry

## 12. Project structure

```
/src
  PresenceSync.App        tray, UI, settings
  PresenceSync.Core       sync logic, state machine (no UI/Graph dependency, testable)
  PresenceSync.Graph      auth (MSAL/WAM), Graph client, retry
/tests
  PresenceSync.Core.Tests sync logic with a mocked presence provider
/docs
  setup-app-registration.md
  consent-request.md
SPEC.md
CLAUDE.md
```

## 13. Implementation order

1. Repo skeleton, `CLAUDE.md` with conventions, CI build (GitHub Actions, `dotnet build` + tests)
2. App registration guide (`docs/setup-app-registration.md`), including an Az CLI or Graph PowerShell script to create it
3. Auth layer with WAM, sign-in per tenant, status display in the tray
4. Read presence and show it in the tooltip (read-only milestone – this allows checking consent in all tenants)
5. Manual control via `setUserPreferredPresence`
6. Auto-sync via `setPresence` including feedback protection, unit tests for the state machine
7. Robustness: backoff, standby/resume, shutdown cleanup, logging
8. Packaging (single file), autostart
9. Phase 2: calendar blockers with dry run

## 14. Acceptance criteria phase 1

- A Teams call in tenant A causes all other active tenants to show "Busy" within 30 s at most
- After the call ends, all tenants are back to their automatic status within 30 s at most
- A DnD set manually in Teams in one tenant is not overwritten by the sync
- The status for all tenants can be set from the tray with at most two clicks
- If one tenant fails (auth, CA, network), the others keep running and the problem is visible in the tray
- Exiting the app leaves no lingering busy status

## 15. Open points

- Determine the consent status per customer tenant (milestone 4)
- Customers' Conditional Access: is a compliant/hybrid-joined device required on my machine? If so, the tenant cannot be used with this app
- Whether `setPresence` with delegated permission reliably aggregates with the Teams desktop session in all tenants needs to be verified in testing. The fallback would be `setUserPreferredPresence` with a short expiration
- Validate a sensible polling interval against Graph throttling

---

## Appendix: implementation notes phase 1 (as of 2026-10-15)

Comparison with the Graph docs (v1.0) and decisions that go beyond the specification:

- **setPresence delegated:** according to the docs supported with `Presence.ReadWrite` (delegated); `sessionId` must be the app's client ID. Aggregation precedence: user-preferred > sessions; among sessions DoNotDisturb > Busy > Available > Away. So the sync overwrites neither a manual DnD nor a manual status – acceptance criterion 3 is covered by the platform.
- **"In a meeting"/OOF** cannot be set via setPresence (docs) – not needed either; only Busy/InACall and DoNotDisturb/Presenting are set.
- **Preferred presence "Unlimited":** without `expirationDuration` the Graph default applies (Busy/DnD 1 day, otherwise 7 days). So "Unlimited" in the menu means "Graph default".
- **Manual status cannot be read back:** `/me/presence` only returns the aggregated state. The app remembers manual statuses it set itself in memory; after a restart it no longer knows about them. Harmless, because preferred presence takes precedence over sessions anyway.
- **Source cooldown (new, `sourceCooldownSeconds`, default 60):** after `clearPresence` or `clearUserPreferredPresence`, Graph may briefly still report the old state. Without protection, a tenant that was just cleared would count as a new source on the next poll (ping-pong). During the cooldown, exactly the previously set state (e.g. Busy/InACall) is not treated as a source; a real, different state (e.g. DnD) is.
- **Setting a manual status** ends an own sync session in the same tenant, so that no stale Busy remains after the manual status expires.
- **Backoff:** 10 s, 20 s, 40 s … max. 5 min per tenant; `Retry-After` takes precedence if larger.
- **Error classes:** `AuthRequired` (yellow, "Sign in …" in the tenant menu), `Disabled` (CA, admin consent, 403 – yellow, tenant paused until signed in/enabled again), `Throttled`/`Transient` (backoff, tenant keeps running).
- **Graph client:** custom `HttpClient` instead of the SDK, rationale in `src/PresenceSync.Graph/GraphClient.cs`.
- **UI and runtime (as of 2026-10-15, added later):** .NET 10 LTS instead of .NET 8 (.NET 8 end of support on 2026-11-10). UI in WPF with WPF-UI (Fluent/Mica, light/dark following Windows) and H.NotifyIcon. Left-clicking the tray icon opens a flyout with tenant cards and quick actions, right-clicking opens the compact menu from section 9. The icon's warning color is orange instead of yellow, to distinguish it from presence yellow ("Away").
- **Token cache:** MSAL cache persisted DPAPI-encrypted via `Microsoft.Identity.Client.Extensions.Msal` (recommended for WAM, contains account metadata).
