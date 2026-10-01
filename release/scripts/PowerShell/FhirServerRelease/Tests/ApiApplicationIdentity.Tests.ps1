. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Public/Add-AadTestAuthEnvironment.ps1')

function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }
function Get-MgOrganization {
    [pscustomobject]@{ VerifiedDomains = @([pscustomobject]@{ IsDefault = $true; Name = 'example.com' }) }
}
function Get-AzContext {
    [pscustomobject]@{
        Subscription = [pscustomobject]@{ Id = 'subscription-id' }
        Account = [pscustomobject]@{ Type = 'ServicePrincipal'; Id = 'test-app' }
    }
}
function Get-Content { '{"users":[],"clientApplications":[{"id":"globalAdminServicePrincipal","roles":[]}]}' }
function Get-AzKeyVault {
    [pscustomobject]@{ ResourceId = '/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.KeyVault/vaults/test-vault' }
}
function Get-SecretVault {
    [pscustomobject]@{
        Name = 'AzureVault'
        ModuleName = 'Az.KeyVault'
        VaultParameters = @{ AZKVaultName = 'test-vault'; SubscriptionId = 'subscription-id' }
        IsDefault = $true
    }
}
function Register-SecretVault {}
function Get-AzADServicePrincipal { [pscustomobject]@{ Id = 'service-principal' } }
function Get-AzRoleAssignment { [pscustomobject]@{ RoleDefinitionName = 'Key Vault Secrets Officer' } }
function Get-ServiceAudience { 'api://test' }
function Connect-MgGraph {}
function Set-FhirServerApiUsers { @() }

# Client-application plumbing (public client path, no roles).
function Get-ApplicationDisplayName { param($EnvironmentName, $AppId) "$EnvironmentName-$AppId" }
function Get-AzureAdApplicationByDisplayName { $null }
function New-FhirServerClientApplicationRegistration {
    param($ApiAppId, $DisplayName, [switch]$PublicClient)
    $script:clientApiAppId = $ApiAppId
    [pscustomobject]@{ AppId = 'client-app'; AppSecret = 'client-secret' }
}
function Grant-ClientAppDelegatedPermissions {}
function New-FhirServerSmartClientReplyUrl {}
function Set-FhirServerClientAppRoleAssignments { param($ApiAppId, $AppId, $AppRoles) $script:roleAssignmentApiAppId = $ApiAppId }
function Set-Secret { param($Name, $Secret) }
function Get-Secret { param($Name) 'secure' }
function Set-AzKeyVaultSecret {}

$credential = New-Object pscredential('test-app', (ConvertTo-SecureString 'test-secret' -AsPlainText -Force))

# 1 - New application: Microsoft Graph never returns the app from the identifier-URI
#     re-query (eventual consistency), yet the registration's returned AppId must be used.
$script:createApiCalls = 0
$script:setRolesCalled = $false
function Get-AzureAdApplicationByIdentifierUri { $null }
function New-FhirServerApiApplicationRegistration {
    param($FhirServiceAudience, $AppRoles)
    $script:createApiCalls++
    [pscustomobject]@{ AppId = 'created-api-app' }
}
function Set-FhirServerApiApplicationRoles { $script:setRolesCalled = $true }

$script:clientApiAppId = $null
$script:roleAssignmentApiAppId = $null
Add-AadTestAuthEnvironment -TestAuthEnvironmentPath 'unused.json' -EnvironmentName 'test' -KeyVaultName 'test-vault' -ResourceGroupName 'test-rg' -TenantAdminCredential $credential -TenantId 'test-tenant' -ClientId 'test-app' -ClientSecret $credential.Password | Out-Null

if ($script:createApiCalls -ne 1) {
    throw 'The API application registration was not invoked exactly once.'
}
if ($script:clientApiAppId -ne 'created-api-app' -or $script:roleAssignmentApiAppId -ne 'created-api-app') {
    throw "The returned API application identity was not used downstream (client='$script:clientApiAppId', roleAssignment='$script:roleAssignmentApiAppId')."
}
if ($script:setRolesCalled) {
    throw 'Roles were rewritten even though the application was just created with them.'
}

# 2 - Existing application: the identifier-URI lookup succeeds, so no new registration is
#     created and the existing AppId drives the role update.
function Get-AzureAdApplicationByIdentifierUri { [pscustomobject]@{ AppId = 'existing-api-app' } }
function New-FhirServerApiApplicationRegistration { throw 'Existing application must not be re-registered.' }
$script:setRolesApiAppId = $null
function Set-FhirServerApiApplicationRoles { param($ApiAppId, $AppRoles) $script:setRolesApiAppId = $ApiAppId }

$script:clientApiAppId = $null
Add-AadTestAuthEnvironment -TestAuthEnvironmentPath 'unused.json' -EnvironmentName 'test' -KeyVaultName 'test-vault' -ResourceGroupName 'test-rg' -TenantAdminCredential $credential -TenantId 'test-tenant' -ClientId 'test-app' -ClientSecret $credential.Password | Out-Null

if ($script:clientApiAppId -ne 'existing-api-app') {
    throw "The existing API application identity was not used downstream (client='$script:clientApiAppId')."
}

Write-Host 'API application identity from registration is used despite Graph re-query returning null.'
