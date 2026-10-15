# Sicherheit

## Schwachstellen melden

Bitte melden Sie Sicherheitslücken **nicht** über öffentliche Issues, sondern vertraulich über GitHub:
[Report a vulnerability](https://github.com/Lars-Level21/presencesync/security/advisories/new) (Private vulnerability
reporting).

Bitte beschreiben Sie betroffene Version, Schritte zur Reproduktion und mögliche Auswirkungen. Meldungen werden nach
Möglichkeit bearbeitet; Reaktionszeiten werden nicht zugesichert (MIT-Lizenz, ohne Gewährleistung).

## Unterstützte Versionen

Sicherheitskorrekturen gibt es für das jeweils neueste Release.

## Sicherheitsmodell in Kürze

- Lokale Windows-App, kein Backend, keine Telemetrie; Netzwerkziele nur `login.microsoftonline.com` und
  `graph.microsoft.com`.
- Nur delegierte Microsoft-Graph-Berechtigungen `User.Read`, `Presence.ReadWrite`, `offline_access`; Zugriff
  ausschließlich auf den Teams-Status des angemeldeten Benutzers.
- Anmeldung über MSAL mit Windows-Token-Broker (WAM); MFA und Conditional Access des jeweiligen Tenants gelten
  uneingeschränkt. Anmeldedialoge öffnen sich nie automatisch.
- Keine Secrets in Code oder Konfiguration; Token werden nicht geloggt, Anmeldenamen im Log maskiert.
- Zentrale Vorgaben über `SOFTWARE\Policies\PresenceSync`, von Windows gegen Änderungen durch Standardbenutzer
  geschützt.
- Build-Pipeline mit Schwachstellenprüfung der Abhängigkeiten; Actions auf Commit-SHAs gepinnt, Dependabot aktiv.

Details: [docs/architecture.md](docs/architecture.md) (Architektur, Datenflüsse, gespeicherte Daten) und
[docs/consent.md](docs/consent.md) (Berechtigungen, Sicherheit aus Sicht eines Kunden-Tenants).

## Bekannte Einschränkungen

Bewusste Entscheidungen, keine offenen Fehler:

| Thema | Beschreibung | Umgang |
| --- | --- | --- |
| Unsignierte Release-Exe | `PresenceSync.exe` in den Releases ist nicht Authenticode-signiert. Windows SmartScreen warnt beim ersten Start einer heruntergeladenen Datei; AppLocker/WDAC können sie nicht per Herausgeber freigeben. | Jedes Release hat eine SHA-256-Prüfsumme (siehe unten). Wer eine geprüfte Herkunft braucht, baut aus dem Quellcode und signiert mit dem eigenen Zertifikat. |
| Entpackte Bibliotheken | Die Einzeldatei-Exe entpackt native Bibliotheken (WAM, WPF) nach `%TEMP%\.net\PresenceSync\…`. Ein Prozess desselben Benutzers könnte sie austauschen. | Setzt bereits Codeausführung im Benutzerkontext voraus – dann sind auch Teams und Office angreifbar. |
| Konfiguration im Klartext | `%APPDATA%\PresenceSync\config.json` enthält Tenant-IDs, Anzeigenamen und Anmeldenamen. | Keine Geheimnisse in der Datei; der Token-Cache ist per Windows-DPAPI an den Benutzer gebunden, Refresh-Token verwaltet Windows (WAM). |

Schadenspotenzial bei kompromittiertem Gerät: Ein Angreifer mit Codeausführung im Benutzerkontext kann über
PresenceSync den Teams-Status **dieses Benutzers** lesen und setzen – keinen Zugriff auf andere Daten oder Benutzer.

## Integrität der Releases

Jedes Release wird per GitHub Actions aus dem getaggten Quellcode gebaut. Neben `PresenceSync.exe` liegt eine
`PresenceSync.exe.sha256`. Prüfen:

```powershell
(Get-FileHash .\PresenceSync.exe -Algorithm SHA256).Hash
```

Der Wert muss mit der `.sha256`-Datei übereinstimmen.
