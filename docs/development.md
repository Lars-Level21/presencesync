# Entwicklung

Bauen, Testen, Projektstruktur und Release. Konventionen für Code und KI-Assistenten: [CLAUDE.md](../CLAUDE.md).
Architektur: [architecture.md](architecture.md).

## Voraussetzungen

- Windows 10/11 x64
- .NET SDK 10 (über `global.json` gepinnt)

## Bauen und testen

```bash
dotnet build
dotnet test
dotnet publish src/PresenceSync.App -c Release -o publish
```

`publish/PresenceSync.exe` ist eine eigenständige Einzeldatei (win-x64), keine Installation nötig. Warnungen gelten
als Fehler (`Directory.Build.props`).

## Projektstruktur

```
src/
  PresenceSync.Core        Sync-Logik, Zustandsmaschine, Konfiguration, Richtlinien – ohne UI/HTTP/MSAL, getestet
  PresenceSync.Graph       Anmeldung (MSAL/WAM), Graph-Client, Fehlerabbildung
  PresenceSync.App         WPF-Oberfläche (WPF-UI), Tray, Einstellungen, Schleife, Windows-Ereignisse
tests/
  PresenceSync.Core.Tests  xUnit, simuliertes Teams (FakePresenceGateway), FakeTimeProvider
deploy/
  admx/                    ADMX/ADML-Vorlagen für Intune und Gruppenrichtlinie
  intune/                  Install-/Uninstall-Skripte für eine Intune-Win32-App
scripts/
  New-AppRegistration.ps1  App-Registrierung anlegen (Azure CLI)
  Set-PresenceSyncPolicy.ps1  Richtlinie setzen/entfernen
docs/                      Dokumentation (Einstieg: README.md)
```

## Laufzeitpfade

| | |
| --- | --- |
| Konfiguration | `%APPDATA%\PresenceSync\config.json` |
| Log | `%LOCALAPPDATA%\PresenceSync\logs` |
| Token-Cache (DPAPI) | `%LOCALAPPDATA%\PresenceSync\msal.cache` |
| Richtlinie | `HKLM` / `HKCU` `\SOFTWARE\Policies\PresenceSync` |

## CI und Release

- **CI** (`.github/workflows/ci.yml`): Build, Tests, Schwachstellenprüfung der NuGet-Pakete, Publish als Artefakt.
  Actions sind auf Commit-SHAs gepinnt; Dependabot hält NuGet-Pakete und Actions aktuell.
- **Release** (`.github/workflows/release.yml`): Ein Tag `v1.2.3` baut, testet, veröffentlicht `PresenceSync.exe`
  mit SHA-256-Prüfsumme und legt das GitHub-Release an. Die Exe ist bewusst **nicht signiert** (siehe
  [SECURITY.md](../SECURITY.md#bekannte-einschränkungen)); wer eine geprüfte Herkunft braucht, baut selbst.
- **Repository-Schutz:** Private vulnerability reporting, Dependabot alerts, Secret scanning mit Push protection;
  `main` nur per Pull Request mit grünem CI-Check `build`.

## Ursprüngliche Anforderungen

[spec.md](spec.md) enthält die Spezifikation, mit der das Projekt gestartet ist, und Umsetzungsnotizen zu
Abweichungen.
