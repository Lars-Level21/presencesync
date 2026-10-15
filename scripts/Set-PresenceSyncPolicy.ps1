<#
.SYNOPSIS
    Sets the PresenceSync organisation policy in the registry (Intune platform/remediation script, GPO startup script,
    software distribution).

.DESCRIPTION
    Writes Software\Policies\PresenceSync:
      ClientId            (REG_SZ)    app registration client ID, mandatory
      HomeTenantId        (REG_SZ)    optional, home tenant ID (GUID); added automatically and signed in with the Windows account
      AllowCustomClientId (REG_DWORD) optional, 1 = users may use another client ID

    Writes the machine policy (HKLM) and therefore needs admin rights (Intune: run as SYSTEM, not in user context).
    Users cannot change it. Per-user policies (HKCU\SOFTWARE\Policies) are protected by Windows as well and can only
    be set by Group Policy or Intune (imported ADMX), not by a script running as the user.

.EXAMPLE
    ./Set-PresenceSyncPolicy.ps1 -ClientId 00000000-0000-0000-0000-000000000000 -HomeTenantId 11111111-1111-1111-1111-111111111111

.EXAMPLE
    ./Set-PresenceSyncPolicy.ps1 -Remove
#>
[CmdletBinding(DefaultParameterSetName = 'Set')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Set')]
    [guid] $ClientId,

    [Parameter(ParameterSetName = 'Set')]
    [guid] $HomeTenantId,

    [Parameter(ParameterSetName = 'Set')]
    [switch] $AllowCustomClientId,

    [Parameter(Mandatory, ParameterSetName = 'Remove')]
    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
$path = 'HKLM:\SOFTWARE\Policies\PresenceSync'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator rights (or SYSTEM) required to write HKLM\SOFTWARE\Policies\PresenceSync.'
}

if ($Remove) {
    if (Test-Path $path) { Remove-Item $path -Recurse }
    Write-Output "PresenceSync policy removed"
    return
}

New-Item -Path $path -Force | Out-Null
Set-ItemProperty -Path $path -Name ClientId -Value $ClientId.ToString() -Type String

if ($PSBoundParameters.ContainsKey('HomeTenantId')) {
    Set-ItemProperty -Path $path -Name HomeTenantId -Value $HomeTenantId.ToString() -Type String
}
else {
    Remove-ItemProperty -Path $path -Name HomeTenantId -ErrorAction SilentlyContinue
}

Set-ItemProperty -Path $path -Name AllowCustomClientId -Value ([int][bool]$AllowCustomClientId) -Type DWord
Write-Output "PresenceSync policy set: ClientId=$ClientId HomeTenantId=$HomeTenantId AllowCustomClientId=$([bool]$AllowCustomClientId)"
