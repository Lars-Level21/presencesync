# PresenceSync – Spezifikation

> Ursprüngliche Anforderungen, mit denen das Projekt gestartet ist (historisch; Dateinamen und Struktur haben sich seitdem geändert). Aktuelle Dokumentation: [README](../README.md). Abweichungen und Entscheidungen stehen im Anhang.

Windows-Tray-App, die den Microsoft-Teams-Status über mehrere Entra-ID-Tenants synchronisiert und manuell steuerbar macht.

## 1. Ausgangslage

- Ein Home-Tenant und drei Kunden-Tenants, in jedem Kunden-Tenant ein eigener Member-Account (kein Gast).
- Im neuen Teams-Client sind alle Accounts parallel angemeldet.
- Presence ist pro Tenant isoliert. Ein Call in Tenant A setzt nur A auf "Beschäftigt", alle anderen bleiben auf "Verfügbar".
- Den Status manuell anzupassen erfordert jedes Mal einen Organisationswechsel im Teams-Client. Das ist im Projektalltag unbrauchbar.

## 2. Ziele

1. **Auto-Sync:** Bin ich in einem Tenant im Call, im Meeting, am Präsentieren oder auf DnD, erscheine ich in allen anderen Tenants automatisch als beschäftigt. Endet der Zustand, fällt der Status von selbst zurück.
2. **Manuelle Steuerung:** Status per Tray-Menü für alle Tenants gleichzeitig oder pro Tenant setzen, ohne Teams-Client.
3. **Phase 2 – Kalender-Blocker:** Termine aus jedem Tenant werden als anonyme Busy-Blocker in die Kalender der anderen Tenants gespiegelt (Free/Busy + kalenderbasierte Presence).

## 3. Nicht-Ziele

- Kein Umgehen von Consent über First-Party-Client-IDs (Teams-Client-ID) oder undokumentierte Endpoints. Nur offizielle Microsoft-Graph-APIs.
- Keine Weitergabe von Inhalten (Betreff, Teilnehmer, Ort) zwischen Kunden-Tenants.
- Kein Server-/Cloud-Backend. Alles läuft lokal auf dem Client.
- Keine Chat- oder Nachrichtenfunktionen.

## 4. Tech-Stack

- .NET 8, WinForms oder WPF mit `NotifyIcon` (kein Hauptfenster nötig, nur ein Einstellungsdialog)
- `Microsoft.Identity.Client` + `Microsoft.Identity.Client.Broker` (WAM)
- `Microsoft.Graph` SDK v5 oder schlanker eigener `HttpClient` – Entscheidung im Code begründen
- Logging: Serilog, Rolling File unter `%LOCALAPPDATA%\PresenceSync\logs`
- Konfiguration: `%APPDATA%\PresenceSync\config.json`
- Auslieferung: self-contained single-file `win-x64`, Autostart über `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (in den Einstellungen abschaltbar)

## 5. App-Registration (Home-Tenant)

- Multi-Tenant (`AzureADMultipleOrgs`), Public Client, keine Secrets
- Redirect URI für WAM: `ms-appx-web://microsoft.aad.brokerplugin/{clientId}`, zusätzlich `http://localhost` als Fallback
- Delegated Permissions:
  - `User.Read`
  - `Presence.ReadWrite` (Phase 1)
  - `Calendars.ReadWrite` (Phase 2, erst anfordern, wenn Phase 2 aktiviert wird – inkrementeller Consent)
  - `offline_access`
- Publisher-Verifizierung über die Partner-Center-ID (MPN), damit User-Consent in Tenants mit der Policy "verified publishers" überhaupt möglich ist
- In jedem Kunden-Tenant wird beim ersten Login ein Service Principal angelegt. Für den Admin-Consent-Fall eine kurze Begründung für die Kunden-IT als `docs/consent-request.md` beilegen (Scope, delegated only, kein Datenzugriff, Einschränkung per "Assignment required" auf meinen User möglich)

## 6. Authentifizierung

- Ein `IPublicClientApplication` pro Tenant mit Authority `https://login.microsoftonline.com/{tenantId}`
- WAM-Broker aktiv (`WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))`), damit die bestehenden Windows-/Teams-Accounts per SSO greifen
- Token-Akquise: `AcquireTokenSilent` mit dem gespeicherten Account (Login-Hint = UPN aus der Config), Fallback `AcquireTokenInteractive` mit Parent-Window-Handle
- Interaktive Re-Auth nie automatisch während eines Calls aufpoppen lassen. Stattdessen Tray-Icon auf "Warnung" setzen und den Tenant im Menü als "Anmeldung erforderlich" markieren
- Fehler aus Conditional Access (z. B. `AADSTS53000` Compliant Device, `AADSTS50076` MFA) sauber erkennen und im Menü/Log mit Klartext anzeigen. Der Tenant läuft dann im Status "deaktiviert" weiter, ohne die anderen zu blockieren

## 7. Konfiguration

```json
{
  "clientId": "<guid>",
  "pollIntervalSeconds": 20,
  "sessionExpirationMinutes": 5,
  "autoSync": true,
  "tenants": [
    { "name": "LEVEL21", "tenantId": "<guid>", "upn": "lars@…", "enabled": true, "isHome": true },
    { "name": "Kunde A", "tenantId": "<guid>", "upn": "lars.martin@kunde-a.…", "enabled": true }
  ],
  "calendarMirror": { "enabled": false, "daysAhead": 14, "intervalMinutes": 10 }
}
```

Tenants werden über den Einstellungsdialog hinzugefügt: Tenant-ID oder Domain eingeben, Login durchführen, UPN übernehmen.

## 8. Phase 1 – Presence-Sync

### 8.1 Graph-Endpoints

- Lesen: `GET /me/presence` → `availability`, `activity`
- Automatischer Sync: `POST /users/{userId}/presence/setPresence`

  ```json
  { "sessionId": "<clientId>", "availability": "Busy", "activity": "InACall", "expirationDuration": "PT5M" }
  ```

  Erlaubte Kombinationen laut Doku prüfen (Available/Available, Busy/InACall, Busy/InAConferenceCall, Away/Away, DoNotDisturb/Presenting). `expirationDuration` min. 5, max. 240 Minuten.
- Session beenden: `POST /users/{userId}/presence/clearPresence` mit `sessionId`
- Manuelle Steuerung: `POST /users/{userId}/presence/setUserPreferredPresence` bzw. `clearUserPreferredPresence`

`setPresence` legt eine eigene App-Session an, die Teams mit den anderen Sessions (Desktop, Mobile) aggregiert. Damit wird nichts hart überschrieben, ein manuell gesetztes DnD bleibt bestehen, und bei einem Absturz der App läuft der Status von selbst aus.

### 8.2 Sync-Logik

Polling-Loop, Standard alle 20 s, parallel über alle aktiven Tenants:

1. Presence aller Tenants lesen.
2. Quell-Tenants bestimmen: Tenants, deren Status "belegt" ist (`activity` ∈ {`InACall`, `InAConferenceCall`, `InAMeeting`, `Presenting`, `UrgentInterruptionsOnly`} oder `availability` = `DoNotDisturb`) und in denen die App aktuell keine eigene Session gesetzt hat.
3. Gibt es mindestens einen Quell-Tenant:
   - In allen anderen aktiven Tenants `setPresence` Busy/InACall (bzw. DoNotDisturb/Presenting, wenn die Quelle DnD/Presenting ist) mit Expiration 5 min.
   - Session wird bei jedem Poll erneuert, solange die Quelle belegt ist (Refresh nur, wenn die Restlaufzeit < 2 min, um API-Calls zu sparen).
4. Gibt es keinen Quell-Tenant mehr: in allen Tenants mit eigener Session `clearPresence` aufrufen (nicht nur auslaufen lassen, damit der Status sofort stimmt).

Feedback-Schleife vermeiden: Ein Tenant, in dem die App selbst eine Session gesetzt hat, darf nie als Quelle gewertet werden. Der Zustand "eigene Session aktiv, seit, bis" wird pro Tenant im Speicher geführt. Bekannte Einschränkung: Startet in einem solchen Tenant parallel ein echter Call, wird das nicht als neue Quelle erkannt. Das ist für den Use Case akzeptabel; im Code als bekannte Einschränkung dokumentieren.

Beim Beenden der App und bei Windows-Logoff/Shutdown (`SessionEnding`) für alle eigenen Sessions `clearPresence` versuchen (best effort mit kurzem Timeout).

### 8.3 Fehlerbehandlung

- HTTP 429 / 503: `Retry-After` respektieren, exponentieller Backoff pro Tenant
- Fehler in einem Tenant darf den Loop für die anderen nicht blockieren
- Netzwerkverlust: Loop pausieren, nach Wiederverbindung neu starten
- Wechsel in Standby/Resume (`SystemEvents.PowerModeChanged`): nach Resume sofort einen Poll auslösen

## 9. Tray-UI

- Icon zeigt den aggregierten Zustand: grün (alle verfügbar), rot (irgendwo belegt / Sync aktiv), grau (Auto-Sync aus), gelb (mindestens ein Tenant mit Auth-Problem)
- Tooltip: kurze Übersicht pro Tenant, z. B. `Kunde A: Im Anruf (Quelle)`, `Kunde B: Beschäftigt (Sync)`
- Kontextmenü:
  - Alle Tenants: Verfügbar · Beschäftigt · Nicht stören · Gleich zurück · Abwesend · Zurücksetzen
  - Bei manuellen Status jeweils eine Dauer wählbar: 30 min · 1 h · 2 h · Bis Tagesende · Unbegrenzt
  - Pro Tenant ein Untermenü mit denselben Optionen und aktueller Statusanzeige
  - Auto-Sync an/aus
  - Einstellungen …
  - Log öffnen
  - Beenden
- Optional: globales Hotkey für "Alle → Nicht stören 1 h" und "Alle → Zurücksetzen" (konfigurierbar)

Manuelle Status gehen über `setUserPreferredPresence` und haben Vorrang vor dem Auto-Sync. Solange ein manueller Status aktiv ist, setzt der Auto-Sync in dem Tenant nichts.

## 10. Phase 2 – Kalender-Blocker

- Pro Tenant `GET /me/calendarView?startDateTime=…&endDateTime=…` (Zeitraum gemäß `daysAhead`)
- Für jeden Termin mit `showAs` ∈ {`busy`, `oof`, `tentative`} in jedem anderen Tenant einen Blocker anlegen:
  - Betreff: konfigurierbar, Default `Blocker` – keine Inhalte aus dem Quell-Tenant (Betreff, Teilnehmer, Ort, Body, Teams-Link)
  - `showAs` wie Quelle, `sensitivity: private`, `isReminderOn: false`, keine Teilnehmer, keine Online-Meeting-Info
  - Zuordnung über eine `singleValueExtendedProperty` (eigene GUID-basierte Property mit Quell-Tenant-ID + Quell-Event-ID-Hash)
- Blocker niemals erneut spiegeln: Events mit der eigenen Extended Property werden beim Lesen ignoriert
- Abgleich: neue Blocker anlegen, geänderte Zeiten aktualisieren, Blocker zu gelöschten/abgesagten Quellterminen entfernen
- Ganztägige Termine und Serien korrekt behandeln (calendarView liefert expandierte Instanzen)
- Zeitzonen: Header `Prefer: outlook.timezone="UTC"` beim Lesen, Blocker in UTC schreiben
- Dry-Run-Modus, der nur loggt, was angelegt/geändert/gelöscht würde

## 11. Sicherheit und Datenschutz

- Keine Secrets, keine Tokens im Klartext. Token-Cache ausschließlich über WAM/MSAL
- Logs enthalten keine Tokens, keine Termininhalte und keine personenbezogenen Daten Dritter. UPNs nur maskiert
- Minimalprinzip: Phase-2-Scope erst anfordern, wenn Phase 2 aktiviert wird
- Kein Telemetrie-Versand

## 12. Projektstruktur

```
/src
  PresenceSync.App        Tray, UI, Settings
  PresenceSync.Core       Sync-Logik, Zustandsmaschine (ohne UI-/Graph-Abhängigkeit, testbar)
  PresenceSync.Graph      Auth (MSAL/WAM), Graph-Client, Retry
/tests
  PresenceSync.Core.Tests Sync-Logik mit gemocktem Presence-Provider
/docs
  setup-app-registration.md
  consent-request.md
SPEC.md
CLAUDE.md
```

## 13. Umsetzungsreihenfolge

1. Repo-Skeleton, `CLAUDE.md` mit Konventionen, CI-Build (GitHub Actions, `dotnet build` + Tests)
2. Anleitung App-Registration (`docs/setup-app-registration.md`), inkl. Az-CLI- oder Graph-PowerShell-Skript zum Anlegen
3. Auth-Layer mit WAM, Login pro Tenant, Statusanzeige im Tray
4. Presence lesen und im Tooltip anzeigen (Read-only-Milestone – damit lässt sich der Consent in allen Tenants prüfen)
5. Manuelle Steuerung über `setUserPreferredPresence`
6. Auto-Sync über `setPresence` inkl. Feedback-Schutz, Unit-Tests für die Zustandsmaschine
7. Robustheit: Backoff, Standby/Resume, Shutdown-Cleanup, Logging
8. Packaging (single-file), Autostart
9. Phase 2: Kalender-Blocker mit Dry-Run

## 14. Akzeptanzkriterien Phase 1

- Ein Teams-Call in Tenant A führt innerhalb von max. 30 s dazu, dass alle anderen aktiven Tenants "Beschäftigt" zeigen
- Nach Ende des Calls sind alle Tenants innerhalb von max. 30 s wieder im automatischen Status
- Ein manuell in Teams gesetztes DnD in einem Tenant wird vom Sync nicht überschrieben
- Status für alle Tenants lässt sich mit max. zwei Klicks aus dem Tray setzen
- Fällt ein Tenant aus (Auth, CA, Netzwerk), laufen die anderen weiter, und das Problem ist im Tray sichtbar
- Beenden der App hinterlässt keine hängenden Busy-Status

## 15. Offene Punkte

- Consent-Status pro Kunden-Tenant ermitteln (Milestone 4)
- Conditional Access der Kunden: Wird auf meinem Gerät ein Compliant/Hybrid-Joined-Device verlangt? Falls ja, ist der Tenant mit dieser App nicht nutzbar
- Ob `setPresence` mit delegated Permission in allen Tenants zuverlässig mit der Teams-Desktop-Session aggregiert, ist im Test zu verifizieren. Fallback wäre `setUserPreferredPresence` mit kurzer Expiration
- Sinnvolles Polling-Intervall gegen Graph-Throttling validieren

---

## Anhang: Umsetzungsnotizen Phase 1 (Stand 2026-10-15)

Abgleich mit der Graph-Doku (v1.0) und Entscheidungen, die über die Spezifikation hinausgehen:

- **setPresence delegated:** laut Doku mit `Presence.ReadWrite` (delegated) unterstützt; `sessionId` muss die Client-ID der App sein. Präzedenz der Aggregation: User-Preferred > Sessions; unter Sessions DoNotDisturb > Busy > Available > Away. Damit überschreibt der Sync weder manuelles DnD noch einen manuellen Status – Akzeptanzkriterium 3 ist durch die Plattform abgedeckt.
- **"In a meeting"/OOF** lassen sich per setPresence nicht setzen (Doku) – wird auch nicht gebraucht, gesetzt werden nur Busy/InACall und DoNotDisturb/Presenting.
- **Preferred Presence "Unbegrenzt":** Ohne `expirationDuration` gilt der Graph-Default (Busy/DnD 1 Tag, sonst 7 Tage). "Unbegrenzt" im Menü heißt also "Graph-Default".
- **Manueller Status ist nicht auslesbar:** `/me/presence` liefert nur den aggregierten Zustand. Die App merkt sich selbst gesetzte manuelle Status im Speicher; nach einem Neustart weiß sie davon nichts mehr. Unschädlich, weil Preferred Presence ohnehin Vorrang vor Sessions hat.
- **Quell-Cooldown (neu, `sourceCooldownSeconds`, Default 60):** Nach `clearPresence` bzw. `clearUserPreferredPresence` kann Graph den alten Zustand noch kurz melden. Ohne Schutz würde ein gerade geleerter Tenant im nächsten Poll als neue Quelle gelten (Ping-Pong). Während des Cooldowns wird genau der zuvor gesetzte Zustand (z. B. Busy/InACall) nicht als Quelle gewertet; ein echter, abweichender Zustand (z. B. DnD) schon.
- **Manueller Status setzen** beendet eine eigene Sync-Session im selben Tenant, damit nach Ablauf des manuellen Status kein veraltetes Busy stehen bleibt.
- **Backoff:** 10 s, 20 s, 40 s … max. 5 min pro Tenant; `Retry-After` hat Vorrang, wenn größer.
- **Fehlerklassen:** `AuthRequired` (gelb, "Anmelden …" im Tenant-Menü), `Disabled` (CA, Admin-Consent, 403 – gelb, Tenant pausiert bis zur erneuten Anmeldung/Aktivierung), `Throttled`/`Transient` (Backoff, Tenant läuft weiter).
- **Graph-Client:** eigener `HttpClient` statt SDK, Begründung in `src/PresenceSync.Graph/GraphClient.cs`.
- **UI und Runtime (Stand 2026-10-15, nachträglich):** .NET 10 LTS statt .NET 8 (Support-Ende .NET 8 am 10.11.2026). Oberfläche in WPF mit WPF-UI (Fluent/Mica, hell/dunkel nach Windows) und H.NotifyIcon. Linksklick aufs Tray-Icon öffnet ein Flyout mit Tenant-Karten und Schnellaktionen, Rechtsklick das kompakte Menü aus Abschnitt 9. Warnfarbe des Icons ist Orange statt Gelb, damit sie sich vom Presence-Gelb ("Abwesend") unterscheidet.
- **Token-Cache:** MSAL-Cache persistiert DPAPI-verschlüsselt über `Microsoft.Identity.Client.Extensions.Msal` (empfohlen für WAM, enthält Account-Metadaten).
