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
function Get-Content {
    if ($script:withUsers) {
        '{"users":[{"roles":["reader","writer"]}],"clientApplications":[]}'
    }
    else {
        '{"users":[],"clientApplications":[]}'
    }
}
function Get-AzKeyVault {
    [pscustomobject]@{ ResourceId = '/subscriptions/subscription-id/resourceGroups/test-rg/providers/Microsoft.KeyVault/vaults/test-vault' }
}
function Get-SecretVault {
    $script:registeredVault
}
function Register-SecretVault { throw 'Provided Name for vault is already being used.' }
function Get-AzADServicePrincipal {
    if ($script:stopAfterVault) {
        throw 'STOP_AFTER_VAULT'
    }
    [pscustomobject]@{ Id = 'service-principal' }
}
function Get-AzRoleAssignment { [pscustomobject]@{ RoleDefinitionName = 'Key Vault Secrets Officer' } }
function Get-ServiceAudience { 'api://test' }
function Connect-MgGraph {}
function Get-AzureAdApplicationByIdentifierUri {
    if ($script:createdRoles.Count -gt 0) {
        [pscustomobject]@{ AppId = 'api-app' }
    }
}
function New-FhirServerApiApplicationRegistration {
    param($FhirServiceAudience, $AppRoles)
    $script:createdRoles = @($AppRoles)
    [pscustomobject]@{ AppId = 'api-app' }
}
function Set-FhirServerApiApplicationRoles { throw 'Unexpected role rewrite after application creation.' }
function Set-FhirServerApiUsers { @() }

$credential = New-Object pscredential('test-app', (ConvertTo-SecureString 'test-secret' -AsPlainText -Force))
$script:registeredVault = [pscustomobject]@{
    Name = 'AzureVault'
    ModuleName = 'Az.KeyVault'
    VaultParameters = @{ AZKVaultName = 'test-vault'; SubscriptionId = 'subscription-id' }
    IsDefault = $true
}
$script:createdRoles = @()
$script:stopAfterVault = $true
$script:withUsers = $false
try {
    Add-AadTestAuthEnvironment -TestAuthEnvironmentPath 'unused.json' -EnvironmentName 'test' -KeyVaultName 'test-vault' -ResourceGroupName 'test-rg' -TenantAdminCredential $credential -TenantId 'test-tenant' -ClientId 'test-app' -ClientSecret $credential.Password | Out-Null
    throw 'Test did not reach vault registration.'
}
catch {
    if ($_.Exception.Message -ne 'STOP_AFTER_VAULT') {
        throw
    }
}

$script:registeredVault.VaultParameters.AZKVaultName = 'other-vault'
try {
    Add-AadTestAuthEnvironment -TestAuthEnvironmentPath 'unused.json' -EnvironmentName 'test' -KeyVaultName 'test-vault' -ResourceGroupName 'test-rg' -TenantAdminCredential $credential -TenantId 'test-tenant' -ClientId 'test-app' -ClientSecret $credential.Password | Out-Null
    throw 'Mismatched vault registration was reused.'
}
catch {
    if ($_.Exception.Message -notlike "*does not match the intended Azure Key Vault*") {
        throw
    }
}

$script:registeredVault.VaultParameters.AZKVaultName = 'test-vault'
$script:stopAfterVault = $false
$script:withUsers = $true
Add-AadTestAuthEnvironment -TestAuthEnvironmentPath 'unused.json' -EnvironmentName 'test' -KeyVaultName 'test-vault' -ResourceGroupName 'test-rg' -TenantAdminCredential $credential -TenantId 'test-tenant' -ClientId 'test-app' -ClientSecret $credential.Password | Out-Null
if (@(Compare-Object $script:createdRoles @('reader', 'writer')).Count -ne 0) {
    throw 'The application was not created with its configured roles.'
}

Write-Host 'Existing matching vault registration reused on task retry.'
