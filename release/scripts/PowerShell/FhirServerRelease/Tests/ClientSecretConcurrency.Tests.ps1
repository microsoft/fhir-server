. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Public/Add-AadTestAuthEnvironment.ps1')

# Environment plumbing (mirrors ApiApplicationIdentity.Tests.ps1) so the script reaches the
# existing-client-application branch that rotates the client secret via Add-MgApplicationPassword.
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
function Get-Content { '{"users":[],"clientApplications":[{"id":"globalAdminServicePrincipal","roles":["globalAdmin"]}]}' }
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

# Existing API application: no new registration, existing identity drives downstream calls.
function Get-AzureAdApplicationByIdentifierUri { [pscustomobject]@{ AppId = 'existing-api-app' } }
function Set-FhirServerApiApplicationRoles {}

# Existing client application: forces the else branch that removes and re-creates the secret.
function Get-ApplicationDisplayName { param($EnvironmentName, $AppId) "$EnvironmentName-$AppId" }
function Get-AzureAdApplicationByDisplayName { [pscustomobject]@{ Id = 'client-object'; AppId = 'client-app' } }
function Get-MgApplication { param($ApplicationId) [pscustomobject]@{ PasswordCredentials = @() } }
function Remove-MgApplicationPassword { param($ApplicationId, $KeyId) }

# Simulates the transient 409 Directory_ConcurrencyViolation raised by Microsoft Graph when a
# task retry runs concurrently with other tenant modifications.
function Add-MgApplicationPassword {
    param($ApplicationId, $PasswordCredential, $ErrorAction)
    $script:passwordAttempts++
    if ($script:passwordPermanentFailure) {
        throw 'Insufficient privileges to complete the operation.'
    }
    if ($script:passwordAttempts -le $script:passwordConflictsBeforeSuccess) {
        throw 'Add-MgApplicationPassword: Error due to concurrent requests being made to the tenant. Directory_ConcurrencyViolation Status: 409 (Conflict)'
    }
    [pscustomobject]@{ SecretText = 'rotated-secret' }
}

function Set-FhirServerClientAppRoleAssignments { param($ApiAppId, $AppId, $AppRoles) }
function Set-Secret { param($Name, $Secret) }
function Get-Secret { param($Name) 'secure' }
function Set-AzKeyVaultSecret {}
function Start-Sleep { param($Seconds) $script:sleeps++ }

$credential = New-Object pscredential('test-app', (ConvertTo-SecureString 'test-secret' -AsPlainText -Force))

function Reset-Counters {
    $script:passwordAttempts = 0
    $script:sleeps = 0
    $script:passwordConflictsBeforeSuccess = 0
    $script:passwordPermanentFailure = $false
}

$invokeArgs = @{
    TestAuthEnvironmentPath = 'unused.json'
    EnvironmentName = 'test'
    KeyVaultName = 'test-vault'
    ResourceGroupName = 'test-rg'
    TenantAdminCredential = $credential
    TenantId = 'test-tenant'
    ClientId = 'test-app'
    ClientSecret = $credential.Password
}

# 1 - Two transient 409 conflicts are retried, then the secret rotation succeeds.
Reset-Counters
$script:passwordConflictsBeforeSuccess = 2
Add-AadTestAuthEnvironment @invokeArgs | Out-Null
if ($script:passwordAttempts -ne 3 -or $script:sleeps -ne 2) {
    throw "Transient 409 Directory_ConcurrencyViolation was not retried exactly twice (passwordAttempts=$script:passwordAttempts sleeps=$script:sleeps)."
}

# 2 - A non-concurrency error is surfaced immediately without retry.
Reset-Counters
$script:passwordPermanentFailure = $true
try {
    Add-AadTestAuthEnvironment @invokeArgs | Out-Null
    throw 'A non-concurrency password error was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Insufficient privileges*' -or $script:passwordAttempts -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

# 3 - A 409 that never clears fails explicitly after bounded retries.
Reset-Counters
$script:passwordConflictsBeforeSuccess = 10
try {
    Add-AadTestAuthEnvironment @invokeArgs | Out-Null
    throw 'An unresolved 409 Directory_ConcurrencyViolation was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Directory_ConcurrencyViolation*' -or $script:passwordAttempts -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

Write-Host 'Client secret rotation retries transient 409 Directory_ConcurrencyViolation and surfaces other errors.'
