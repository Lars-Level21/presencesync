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

- Lokale Windows-App, kein Backend, keine Telemetrie.
- Nur delegierte Microsoft-Graph-Berechtigungen `User.Read`, `Presence.ReadWrite`, `offline_access`; Zugriff
  ausschließlich auf den Teams-Status des angemeldeten Benutzers.
- Anmeldung über MSAL mit Windows-Token-Broker (WAM); Conditional Access des jeweiligen Tenants gilt uneingeschränkt.
- Keine Secrets in Code oder Konfiguration; Token werden nicht geloggt.

Details: [docs/architecture.md](docs/architecture.md) (Architektur, Datenflüsse), [docs/consent.md](docs/consent.md) (Berechtigungen) und
[docs/security-review.md](docs/security-review.md) (Review-Ergebnis und offene Punkte).

## Integrität der Releases

Jedes Release wird per GitHub Actions aus dem getaggten Quellcode gebaut. Neben `PresenceSync.exe` liegt eine
`PresenceSync.exe.sha256`. Prüfen:

```powershell
(Get-FileHash .\PresenceSync.exe -Algorithm SHA256).Hash
```

Der Wert muss mit der `.sha256`-Datei übereinstimmen.
