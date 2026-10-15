<#
.SYNOPSIS
    Creates the PresenceSync app registration in the home tenant (Azure CLI).

.DESCRIPTION
    - Multi-tenant (AzureADMultipleOrgs), public client, no secrets
    - Redirect URIs: ms-appx-web://microsoft.aad.brokerplugin/{clientId} (WAM) and http://localhost (browser fallback)
    - Delegated Microsoft Graph permissions: User.Read, Presence.ReadWrite, offline_access
      (Calendars.ReadWrite is deliberately NOT added; Phase 2 requests it via incremental consent)
    - Creates the service principal in the home tenant
    - Optional: sets the verified publisher (Partner ID of type PartnerGlobal, formerly MPN ID)

    Requires: Azure CLI, signed in to the home tenant with a role that can create applications
    (az login --tenant <home-tenant> --allow-no-subscriptions).

.EXAMPLE
    ./scripts/New-AppRegistration.ps1 -DisplayName "PresenceSync" -MpnId 1234567
#>
[CmdletBinding()]
param(
    [string] $DisplayName = 'PresenceSync',

    # Partner ID of type "PartnerGlobal" (Partner Center > Account settings > Identifiers). The location ID
    # (PartnerLocation) is rejected. The app's publisher domain must match the Partner Center primary contact domain,
    # see docs/deployment.md, section "Herausgeber-Verifizierung".
    [string] $MpnId
)

$ErrorActionPreference = 'Stop'
$graphAppId = '00000003-0000-0000-c000-000000000000'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed ($LASTEXITCODE)" }
    $output
}

$tenantId = Invoke-Az account show --query tenantId -o tsv
Write-Host "Home tenant: $tenantId"

function Get-ScopeId([string] $name) {
    $id = Invoke-Az ad sp show --id $graphAppId --query "oauth2PermissionScopes[?value=='$name'].id | [0]" -o tsv
    if (-not $id) { throw "Graph scope $name not found" }
    $id
}

$access = @(
    [ordered]@{
        resourceAppId  = $graphAppId
        resourceAccess = @(
            @{ id = (Get-ScopeId 'User.Read');          type = 'Scope' }
            @{ id = (Get-ScopeId 'Presence.ReadWrite'); type = 'Scope' }
            @{ id = (Get-ScopeId 'offline_access');     type = 'Scope' }
        )
    }
)
$accessFile = New-TemporaryFile
ConvertTo-Json -InputObject $access -Depth 5 | Set-Content $accessFile -Encoding utf8

try {
    $appId = Invoke-Az ad app create `
        --display-name $DisplayName `
        --sign-in-audience AzureADMultipleOrgs `
        --is-fallback-public-client true `
        --required-resource-accesses "@$accessFile" `
        --query appId -o tsv
}
finally {
    Remove-Item $accessFile -ErrorAction SilentlyContinue
}
Write-Host "App created: $appId"

Invoke-Az ad app update --id $appId `
    --public-client-redirect-uris "ms-appx-web://microsoft.aad.brokerplugin/$appId" "http://localhost" | Out-Null
Write-Host 'Redirect URIs set (WAM + http://localhost)'

Invoke-Az ad sp create --id $appId | Out-Null
Write-Host 'Service principal created in home tenant'

if ($MpnId) {
    $objectId = Invoke-Az ad app show --id $appId --query id -o tsv
    $body = New-TemporaryFile
    @{ verifiedPublisherId = $MpnId } | ConvertTo-Json | Set-Content $body -Encoding utf8
    try {
        Invoke-Az rest --method POST `
            --uri "https://graph.microsoft.com/v1.0/applications/$objectId/setVerifiedPublisher" `
            --headers 'Content-Type=application/json' --body "@$body" | Out-Null
        Write-Host "Verified publisher set (MPN $MpnId)"
    }
    finally {
        Remove-Item $body -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host 'Next steps:'
Write-Host "  1. clientId in %APPDATA%\PresenceSync\config.json: $appId"
Write-Host "  2. Admin consent link for customer tenants: https://login.microsoftonline.com/<tenant>/adminconsent?client_id=$appId"
if (-not $MpnId) {
    Write-Host '  3. Verify publisher (Entra portal > App registrations > Branding & properties), see docs/deployment.md'
}
