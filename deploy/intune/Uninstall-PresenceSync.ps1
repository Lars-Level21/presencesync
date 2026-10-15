<#
.SYNOPSIS
    Removes PresenceSync from a device (Intune Win32 app, uninstall context: System).

.DESCRIPTION
    Stops the app, removes %ProgramFiles%\PresenceSync and the autostart entry. -RemovePolicy also removes
    HKLM\SOFTWARE\Policies\PresenceSync. Per-user data (%APPDATA%\PresenceSync, %LOCALAPPDATA%\PresenceSync) and the
    per-user autostart entry stay untouched; the per-user entry points to a missing file and is ignored by Windows.
    Status sessions the app had set expire on their own after at most 5 minutes.
#>
[CmdletBinding()]
param([switch] $RemovePolicy)

$ErrorActionPreference = 'Stop'
Get-Process PresenceSync -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item (Join-Path $env:ProgramFiles 'PresenceSync') -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name PresenceSync -ErrorAction SilentlyContinue
if ($RemovePolicy) {
    Remove-Item 'HKLM:\SOFTWARE\Policies\PresenceSync' -Recurse -ErrorAction SilentlyContinue
}
Write-Output 'PresenceSync removed'
