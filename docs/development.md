# Development

Building, testing, project structure and release. Conventions for code and AI assistants: [CLAUDE.md](../CLAUDE.md).
Architecture: [architecture.md](architecture.md).

## Prerequisites

- Windows 10/11 x64
- .NET SDK 10 (pinned via `global.json`)

## Build and test

```bash
dotnet build
dotnet test
dotnet publish src/PresenceSync.App -c Release -o publish
```

`publish/PresenceSync.exe` is a self-contained single file (win-x64); no installation required. Warnings are treated
as errors (`Directory.Build.props`).

## Project structure

```
src/
  PresenceSync.Core        sync logic, state machine, configuration, policies – no UI/HTTP/MSAL, tested
  PresenceSync.Graph       sign-in (MSAL/WAM), Graph client, error mapping
  PresenceSync.App         WPF UI (WPF-UI), tray, settings, loop, Windows events
tests/
  PresenceSync.Core.Tests  xUnit, simulated Teams (FakePresenceGateway), FakeTimeProvider
deploy/
  admx/                    ADMX/ADML templates for Intune and Group Policy
  intune/                  install/uninstall scripts for an Intune Win32 app
scripts/
  New-AppRegistration.ps1  create the app registration (Azure CLI)
  Set-PresenceSyncPolicy.ps1  set/remove the policy
  Update-Strings.ps1       regenerate the Strings class after editing the .resx resources
docs/                      documentation (entry point: README.md)
```

## Runtime paths

| | |
| --- | --- |
| Configuration | `%APPDATA%\PresenceSync\config.json` |
| Log | `%LOCALAPPDATA%\PresenceSync\logs` |
| Token cache (DPAPI) | `%LOCALAPPDATA%\PresenceSync\msal.cache` |
| Policy | `HKLM` / `HKCU` `\SOFTWARE\Policies\PresenceSync` |

## Localization

The UI follows the Windows display language: English (default) and German. Texts live in
`src/PresenceSync.Core/Resources/Strings.resx` (English) and `Strings.de.resx` (German). To add a text, add the key to both
files and run `scripts/Update-Strings.ps1`. Another language = another `Strings.<culture>.resx`; `LocalizationTests` makes
sure keys and placeholders match. Logs are always English.

## CI and release

- **CI** (`.github/workflows/ci.yml`): build, tests, vulnerability check of NuGet packages, publish as artifact.
  Actions are pinned to commit SHAs; Dependabot keeps NuGet packages and Actions up to date.
- **Release** (`.github/workflows/release.yml`): a tag `v1.2.3` builds, tests, publishes `PresenceSync.exe` with a
  SHA-256 checksum and creates the GitHub release. The exe is deliberately **not signed** (see
  [SECURITY.md](../SECURITY.md#known-limitations)); if you need verified provenance, build it yourself.
- **Repository protection:** private vulnerability reporting, Dependabot alerts, secret scanning with push protection;
  `main` only via pull request with a green CI check `build`.

## Original requirements

[spec.md](spec.md) contains the specification the project started with, plus implementation notes on deviations.
