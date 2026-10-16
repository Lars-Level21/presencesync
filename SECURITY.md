# Security

## Reporting vulnerabilities

Please do **not** report security vulnerabilities through public issues; report them confidentially via GitHub:
[Report a vulnerability](https://github.com/Lars-Level21/presencesync/security/advisories/new) (private vulnerability
reporting).

Please describe the affected version, steps to reproduce and the potential impact. Reports are handled on a
best-effort basis; no response times are guaranteed (MIT license, no warranty).

## Supported versions

Security fixes are provided for the latest release.

## Security model in brief

- Local Windows app, no backend, no telemetry; the only network endpoints are `login.microsoftonline.com` and
  `graph.microsoft.com`.
- Only the delegated Microsoft Graph permissions `User.Read`, `Presence.ReadWrite`, `offline_access`; access
  exclusively to the signed-in user's Teams status.
- Sign-in via MSAL with the Windows token broker (WAM); MFA and Conditional Access of each tenant apply without
  restriction. Sign-in dialogs never open automatically.
- No secrets in code or configuration; tokens are never logged, sign-in names are masked in the log.
- Central settings via `SOFTWARE\Policies\PresenceSync`, protected by Windows against changes by standard users.
- Build pipeline with vulnerability scanning of dependencies; Actions pinned to commit SHAs, Dependabot enabled.

Details: [docs/architecture.md](docs/architecture.md) (architecture, data flows, stored data) and
[docs/consent.md](docs/consent.md) (permissions, security from a customer tenant's perspective).

## Known limitations

Deliberate decisions, not open bugs:

| Topic | Description | Mitigation |
| --- | --- | --- |
| Unsigned release exe | `PresenceSync.exe` in the releases is not Authenticode-signed. Windows SmartScreen warns the first time a downloaded file is run; AppLocker/WDAC cannot allow it by publisher. | Every release has a SHA-256 checksum (see below). If you need verified provenance, build from source and sign with your own certificate. |
| Extracted libraries | The single-file exe extracts native libraries (WAM, WPF) to `%TEMP%\.net\PresenceSync\…`. A process running as the same user could replace them. | Requires code execution in the user context already – at that point Teams and Office are vulnerable too. |
| Plain-text configuration | `%APPDATA%\PresenceSync\config.json` contains tenant IDs, display names and sign-in names. | No secrets in the file; the token cache is bound to the user via Windows DPAPI, refresh tokens are managed by Windows (WAM). |

Impact if the device is compromised: an attacker with code execution in the user context can use PresenceSync to read
and set the Teams status of **this user** – no access to other data or users.

## Release integrity

Every release is built by GitHub Actions from the tagged source code. Next to `PresenceSync.exe` there is a
`PresenceSync.exe.sha256`. To verify:

```powershell
(Get-FileHash .\PresenceSync.exe -Algorithm SHA256).Hash
```

The value must match the `.sha256` file.
