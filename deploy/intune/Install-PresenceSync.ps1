<#
.SYNOPSIS
    Installs PresenceSync for all users of a device (Intune Win32 app, install context: System).

.DESCRIPTION
    - Copies PresenceSync.exe (from the package folder) to %ProgramFiles%\PresenceSync
    - Registers autostart for all users (HKLM\...\Run); the app is single-instance, a per-user entry does no harm
    - Optional: sets the organisation policy (client ID, home tenant) via Set-PresenceSyncPolicy.ps1

    Intune: install command
      powershell.exe -ExecutionPolicy Bypass -NoProfile -File .\Install-PresenceSync.ps1 -ClientId <guid> -HomeTenantId <guid>
    Detection rule: file %ProgramFiles%\PresenceSync\PresenceSync.exe exists (or version >= release version).
#>
[CmdletBinding()]
param(
    [guid] $ClientId,
    [guid] $HomeTenantId
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $env:ProgramFiles 'PresenceSync'
$exe = Join-Path $target 'PresenceSync.exe'

Get-Process PresenceSync -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'PresenceSync.exe') $exe -Force

New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name PresenceSync `
    -Value "`"$exe`"" -PropertyType String -Force | Out-Null

if ($PSBoundParameters.ContainsKey('ClientId')) {
    $policyArgs = @{ ClientId = $ClientId }
    if ($PSBoundParameters.ContainsKey('HomeTenantId')) { $policyArgs.HomeTenantId = $HomeTenantId }
    & (Join-Path $PSScriptRoot 'Set-PresenceSyncPolicy.ps1') @policyArgs
}

Write-Output "PresenceSync installed to $target"
