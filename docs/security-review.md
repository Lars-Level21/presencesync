# Security-Review PresenceSync

| | |
| --- | --- |
| Stand | 15.10.2026, Version 0.1.0 |
| Umfang | Gesamter Quellcode (`src/`, `tests/`), Build- und Release-Pipeline (`.github/`), App-Registrierung (`scripts/`, `docs/`), NuGet-Abhängigkeiten |
| Methode | Manueller Code-Review entlang der Datenflüsse (Anmeldung, Token, Graph-Aufrufe, lokale Speicherung, Logs, Prozess-/Registry-Zugriffe), Abhängigkeitsscan (`dotnet list package --vulnerable --include-transitive`), Abgleich der Graph-Berechtigungen mit der Microsoft-Dokumentation |
| Nicht im Umfang | Penetrationstest, Prüfung von Windows/WAM/MSAL selbst, Konfiguration der einzelnen Kunden-Tenants |

## Ergebnis

Keine kritischen oder hohen Befunde. Die App arbeitet ausschließlich mit **delegierten Low-Privilege-Berechtigungen**
(`User.Read`, `Presence.ReadWrite`, `offline_access`) auf den **eigenen** Status des angemeldeten Benutzers, hat
kein Backend, sendet keine Telemetrie und verbindet sich nur mit Microsoft-Endpunkten. Vier niedrige Befunde wurden
im Rahmen des Reviews behoben; die fehlende Code-Signatur ist eine bewusste Entscheidung (Befund 5).

## Befunde

| # | Bereich | Befund | Bewertung | Status |
| --- | --- | --- | --- | --- |
| 1 | Logging | Fehlermeldungen von MSAL/HTTP werden mit Exception-Text geloggt. Fremdtexte könnten E-Mail-Adressen oder Token-Fragmente enthalten, die die App nicht kontrolliert. | Niedrig | **Behoben:** Jede Logzeile läuft durch `LogRedaction` (UPNs maskiert, JWT/Bearer-Token entfernt), Tests in `SecurityTests`. |
| 2 | Eingaben | Die Tenant-Eingabe im Einstellungsdialog ging ungeprüft in die Authority-URL (z. B. `common`, `organizations`, Pfadteile). | Niedrig | **Behoben:** `TenantInput` erlaubt nur GUID oder Domain; zusätzlich Prüfung in `TenantAuthenticator`. `organizations` wird nur intern für die einmalige Kontoauswahl beim Hinzufügen verwendet (`TenantAuthenticator.ForAccountSelection`), danach läuft jeder Tenant mit eigener, fester Authority. |
| 3 | Graph-Aufrufe | Die Benutzer-ID aus der Graph-Antwort wurde unescaped in den URL-Pfad eingesetzt. | Niedrig | **Behoben:** `Uri.EscapeDataString`. |
| 4 | Lieferkette | GitHub Actions über Tags statt Commit-SHAs referenziert, kein automatischer Schwachstellen-Check. | Niedrig | **Behoben:** Actions auf SHAs gepinnt, `persist-credentials: false`, Schwachstellen-Check im CI bricht den Build ab, Dependabot für NuGet und Actions. |
| 5 | Auslieferung | `PresenceSync.exe` ist nicht Authenticode-signiert. Folge: SmartScreen-Warnung, keine Herkunftsprüfung, in verwalteten Umgebungen (AppLocker/WDAC) schwer freizugeben. | Mittel (für öffentliche Verteilung) | **Akzeptiert:** Open-Source-Werkzeug ohne Herausgeber-Infrastruktur. Jedes Release hat eine SHA-256-Prüfsumme; wer eine geprüfte Herkunft braucht (z. B. für AppLocker/WDAC per Herausgeber), baut aus dem Quellcode und signiert selbst. |
| 6 | Mandanten-Sicht | Öffentlicher Client: Jeder kann mit der Client-ID Token für **sich selbst** anfordern. Ein tenantweiter Admin-Consent gilt für alle Benutzer des Kunden-Tenants. | Mittel (organisatorisch) | **Mitigiert durch Doku:** Admin-Leitfaden empfiehlt *Zuweisung erforderlich* bzw. Consent nur für einen Benutzer (`consentType = Principal`). |
| 7 | App-Registrierung | Keine Datenschutz- und Nutzungsbedingungen-URL hinterlegt. | – | **Entfällt:** Der Herausgeber verarbeitet keine Daten (kein Backend, keine Telemetrie); die Datenverarbeitung auf dem Gerät ist in `architecture.md` beschrieben. |
| 8 | Laufzeit | Die Single-File-Exe entpackt native Bibliotheken (WAM, WPF) nach `%TEMP%\.net\PresenceSync\…`. Ein Prozess desselben Benutzers könnte sie austauschen. | Niedrig | **Akzeptiert:** setzt bereits Code-Ausführung im Benutzerkontext voraus; dann ist auch WAM/Teams kompromittierbar. |
| 9 | Lokale Daten | `config.json` enthält Tenant-IDs, Anzeigenamen und UPNs im Klartext; MSAL-Cache ist per DPAPI an den Windows-Benutzer gebunden. | Niedrig | **Akzeptiert:** keine Geheimnisse in der Config; Refresh-Token liegen bei WAM im Windows-Broker. |
| 10 | Lizenz | Für eine öffentliche Veröffentlichung fehlte eine Lizenz. | – | **Behoben:** MIT-Lizenz (`LICENSE`). |

## Positiv geprüft

- **Berechtigungen minimal:** Nur `User.Read`, `Presence.ReadWrite`, `offline_access`, alle delegiert, alle ohne
  Admin-Pflicht laut Microsoft. Keine Application Permissions, keine Secrets/Zertifikate. `Calendars.ReadWrite`
  (Phase 2) ist bewusst nicht registriert und würde nur per inkrementellem Consent angefordert.
- **Nur eigener Status:** Schreibzugriffe gehen ausschließlich an `/users/{eigene ID}/presence/*`; die ID stammt aus
  `GET /me/presence` des angemeldeten Benutzers.
- **Netzwerk:** Ein fest kodierter Graph-Endpunkt (`https://graph.microsoft.com/v1.0/`), Authority fest auf die
  Azure Public Cloud; TLS-Validierung des Betriebssystems, keine Zertifikats-Ausnahmen, kein Proxy-Code.
- **Token:** Werden nur im Speicher gehalten und nur im `Authorization`-Header an Graph gesendet; nie geloggt, nie
  auf Platte (außer im MSAL/DPAPI-Cache). Bei 401 genau ein Refresh, dann Abbruch.
- **Keine automatische interaktive Anmeldung:** Anmeldedialoge nur nach Klick des Benutzers – schützt vor
  MFA-/Consent-Müdigkeit und Prompt-Spam.
- **Conditional Access wird respektiert:** CA-Fehler (`AADSTS53000` ff.) setzen den Tenant auf *Gesperrt*; es gibt
  keinen Umgehungsversuch, keine First-Party-Client-IDs, keine undokumentierten Endpunkte.
- **Fail-safe-Status:** Vom Sync gesetzte Sessions laufen nach 5 Minuten automatisch ab; beim Beenden und bei
  Abmeldung/Herunterfahren werden sie aktiv gelöscht.
- **Lokale Prozesse/Registry:** `Process.Start` nur mit app-eigenen Pfaden (Log, Config); Autostart nur in
  `HKCU\…\Run` mit gequotetem Pfad; keine Admin-Rechte nötig.
- **Zentrale Vorgaben:** Client-ID und Home-Tenant lassen sich per Richtlinie unter `SOFTWARE\Policies\PresenceSync`
  festlegen; dieser Schlüssel ist von Windows gegen Änderungen durch Standardbenutzer geschützt (auch unter HKCU).
  Die automatische Anmeldung mit dem Windows-Konto gilt nur für den per Richtlinie verwalteten Home-Tenant und nur für
  Mitgliedskonten genau dieses Tenants.
- **Deserialisierung:** `System.Text.Json` ohne Polymorphie/Typnamen.
- **Abhängigkeiten:** Keine bekannten Schwachstellen (Stand 15.10.2026). Einziger Hinweis: `xunit` 2.x ist als
  *Legacy* markiert (nur Testprojekt, keine Auslieferung).

## Restrisiken

- Wer Code im Kontext des angemeldeten Windows-Benutzers ausführen kann, kann über WAM Token für diese App beziehen –
  das gilt aber genauso für Teams, Outlook und jede andere Office-App auf dem Gerät. Schadenspotenzial über diese
  App: Lesen und Setzen des **eigenen** Teams-Status.
- Die App vertraut darauf, dass Microsoft Graph den Presence-Status korrekt zusammenführt (Session-Aggregation). Ein
  Fehlverhalten dort wirkt sich nur auf die Statusanzeige aus.

## Empfehlungen

1. ~~Lizenz festlegen~~ – erledigt (MIT).
2. ~~GitHub *Private vulnerability reporting*, Secret scanning, Branch-Schutz~~ – erledigt.
3. Dieses Review bei Phase 2 (Kalenderzugriff) wiederholen – dann kommen Termin-Metadaten als neue Datenkategorie hinzu.
