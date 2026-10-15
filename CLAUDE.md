# PresenceSync – Conventions

Windows tray app that syncs the Teams status across multiple Entra ID tenants. Entry point: [README.md](README.md), original requirements: [docs/spec.md](docs/spec.md). Docs structure: README → docs/architecture.md, docs/deployment.md (deploying organizations), docs/consent.md (customer tenant admins), docs/development.md.

## Build & test

```bash
dotnet build
dotnet test
dotnet publish src/PresenceSync.App -c Release -o publish   # self-contained single file win-x64
```

SDK: .NET 10 LTS (pinned via `global.json`). Warnings are errors (`Directory.Build.props`).

## Structure

| Project | Contents | Dependencies |
| --- | --- | --- |
| `src/PresenceSync.Core` | Sync state machine (`SyncPlanner` pure, `SyncEngine` stateful), config, strings, error catalog | only `Microsoft.Extensions.Logging.Abstractions` – **no** UI, MSAL or HTTP dependency |
| `src/PresenceSync.Graph` | MSAL/WAM per tenant (`TenantAuthenticator`), lean Graph client, `GraphPresenceGateway` | Core, MSAL |
| `src/PresenceSync.App` | WPF with WPF-UI (Fluent) and H.NotifyIcon: tray flyout, context menu, settings window, loop, system events, Serilog; MVVM with CommunityToolkit.Mvvm | Core, Graph |
| `tests/PresenceSync.Core.Tests` | xUnit, `FakePresenceGateway` simulates Teams aggregation, `FakeTimeProvider` | Core |

## Rules

- Sync decisions belong in `SyncPlanner` (pure function) and are tested there. `SyncEngine` executes and holds state.
- Graph errors are mapped to `TenantAccessException` (`AuthRequired`, `Disabled`, `Throttled`, `Transient`) in the Graph project; Core knows nothing about MSAL/HTTP.
- Configuration sources in this order of precedence: policy HKLM > policy HKCU (`SOFTWARE\Policies\PresenceSync`) > `config.json`. Logic in `PolicyApplier` (Core, tested), reading in `RegistryPolicySource` (App). Re-apply the policy before every save.
- Time only via `TimeProvider` (testable), never `DateTime.Now` in Core.
- Only official Graph v1.0 endpoints, no first-party client IDs.
- Interactive sign-in only on explicit user action (flyout, tray menu, settings), never automatically.
- UI: WPF-UI controls (`ui:CardControl`, `ui:ToggleSwitch`, `ui:Button` …) and theme brushes via `DynamicResource`
  (e.g. `TextFillColorSecondaryBrush`); status icons (color + shape as in Teams) only via `Theme.Image`/`Theme.TrayIcon` with `PresenceGlyph`. Menus are built centrally
  in `MenuFactory`. Engine events come from background threads → `Dispatcher.BeginInvoke`.
- Every scrollable area gets `app:WheelScrolling.Proportional="True"` on its `ScrollViewer`: WPF otherwise scrolls a full step per wheel message, which is far too fast with high-resolution mouse wheels.
- Logs: no tokens, no appointment content, UPNs only via `Mask.Upn`. Tenant display names are fine.
- Never block the UI thread on tasks, except via `Task.Run(...).Wait()` (no SynchronizationContext → no deadlock).
- User-facing strings come from resources (`src/PresenceSync.Core/Resources/Strings.resx` = English default, `Strings.de.resx` = German) via the `Strings` class – never hard-coded in code or XAML. Logs, code comments and identifiers are English. Add a key to both `.resx` files, then run `scripts/Update-Strings.ps1` to regenerate the `Strings` properties; `LocalizationTests` checks that keys, properties and placeholders match in both languages. Errors shown to users are `TenantAccessException`s built from a `LocalizedMessage`: `UserMessage` in the UI language, `Message` in English for the log.
- The UI is shown in the user's Windows display language (English by default, German if Windows is set to German).
- New Graph calls: check allowed availability/activity combinations against the docs (`PresenceRules`, `ManualStatusExtensions`).

## Runtime paths

- Config: `%APPDATA%\PresenceSync\config.json` (example: [docs/config.sample.json](docs/config.sample.json))
- Logs: `%LOCALAPPDATA%\PresenceSync\logs`
- MSAL cache (DPAPI): `%LOCALAPPDATA%\PresenceSync\msal.cache`
