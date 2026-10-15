# PresenceSync – Konventionen

Windows-Tray-App, die den Teams-Status über mehrere Entra-ID-Tenants synchronisiert. Einstieg: [README.md](README.md), ursprüngliche Anforderungen: [docs/spec.md](docs/spec.md). Doku-Struktur: README → docs/architecture.md, docs/deployment.md (bereitstellende Organisationen), docs/consent.md (Admins der Kunden-Tenants), docs/development.md.

## Build & Test

```bash
dotnet build
dotnet test
dotnet publish src/PresenceSync.App -c Release -o publish   # self-contained single file win-x64
```

SDK: .NET 10 LTS (über `global.json` gepinnt). Warnungen sind Fehler (`Directory.Build.props`).

## Struktur

| Projekt | Inhalt | Abhängigkeiten |
| --- | --- | --- |
| `src/PresenceSync.Core` | Sync-Zustandsmaschine (`SyncPlanner` rein, `SyncEngine` zustandsbehaftet), Config, Texte, Fehlerkatalog | nur `Microsoft.Extensions.Logging.Abstractions` – **keine** UI-, MSAL- oder HTTP-Abhängigkeit |
| `src/PresenceSync.Graph` | MSAL/WAM pro Tenant (`TenantAuthenticator`), schlanker Graph-Client, `GraphPresenceGateway` | Core, MSAL |
| `src/PresenceSync.App` | WPF mit WPF-UI (Fluent) und H.NotifyIcon: Tray-Flyout, Kontextmenü, Einstellungsfenster, Loop, System-Events, Serilog; MVVM mit CommunityToolkit.Mvvm | Core, Graph |
| `tests/PresenceSync.Core.Tests` | xUnit, `FakePresenceGateway` simuliert Teams-Aggregation, `FakeTimeProvider` | Core |

## Regeln

- Sync-Entscheidungen gehören in `SyncPlanner` (pure function) und werden dort getestet. `SyncEngine` führt aus und hält Zustand.
- Graph-Fehler werden im Graph-Projekt auf `TenantAccessException` (`AuthRequired`, `Disabled`, `Throttled`, `Transient`) gemappt; Core kennt kein MSAL/HTTP.
- Konfigurationsquellen in dieser Rangfolge: Richtlinie HKLM > Richtlinie HKCU (`SOFTWARE\Policies\PresenceSync`) > `config.json`. Logik in `PolicyApplier` (Core, getestet), Lesen in `RegistryPolicySource` (App). Richtlinie vor jedem Speichern erneut anwenden.
- Zeit nur über `TimeProvider` (testbar), nie `DateTime.Now` in Core.
- Nur offizielle Graph-v1.0-Endpoints, keine First-Party-Client-IDs.
- Interaktive Anmeldung nur auf explizite Benutzeraktion (Flyout, Tray-Menü, Einstellungen), nie automatisch.
- UI: WPF-UI-Controls (`ui:CardControl`, `ui:ToggleSwitch`, `ui:Button` …) und Theme-Brushes per `DynamicResource`
  (z. B. `TextFillColorSecondaryBrush`); Status-Symbole (Farbe + Form wie in Teams) nur über `Theme.Image`/`Theme.TrayIcon` mit `PresenceGlyph`. Menüs entstehen zentral
  in `MenuFactory`. Engine-Events kommen aus Hintergrund-Threads → `Dispatcher.BeginInvoke`.
- Logs: keine Tokens, keine Termininhalte, UPNs nur über `Mask.Upn`. Tenant-Anzeigenamen sind ok.
- Kein Blockieren des UI-Threads auf Tasks, außer über `Task.Run(...).Wait()` (kein SynchronizationContext → kein Deadlock).
- Texte für Benutzer auf Deutsch, Code/Bezeichner auf Englisch.
- Neue Graph-Aufrufe: erlaubte availability/activity-Kombinationen gegen die Doku prüfen (`PresenceRules`, `ManualStatusExtensions`).

## Laufzeitpfade

- Config: `%APPDATA%\PresenceSync\config.json` (Beispiel: [docs/config.sample.json](docs/config.sample.json))
- Logs: `%LOCALAPPDATA%\PresenceSync\logs`
- MSAL-Cache (DPAPI): `%LOCALAPPDATA%\PresenceSync\msal.cache`
