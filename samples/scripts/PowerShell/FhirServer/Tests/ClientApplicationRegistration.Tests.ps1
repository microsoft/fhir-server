$moduleRoot = Split-Path -Parent $PSScriptRoot
Get-ChildItem "$moduleRoot/Private/*.ps1" | ForEach-Object { . $_.FullName }
. "$moduleRoot/Public/New-FhirServerClientApplicationRegistration.ps1"

function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }

# Simulates Microsoft Graph eventual consistency: the API application is not queryable
# for the first $script:apiMissesBeforeFound reads after creation.
function Get-MgApplication {
    param($Filter, $ApplicationId, $ErrorAction)
    $script:apiReads++
    if ($script:apiReads -le $script:apiMissesBeforeFound) {
        return $null
    }
    [pscustomobject]@{
        AppId = 'api-app'
        Id = 'api-object'
        IdentifierUris = @('api://test')
        Api = [pscustomobject]@{ Oauth2PermissionScopes = @([pscustomobject]@{ Id = 'scope-id' }) }
    }
}

function New-MgApplication {
    param($DisplayName)
    [pscustomobject]@{
        AppId = 'client-app'
        Id = 'client-object'
        Web = [pscustomobject]@{ RedirectUris = @('https://example.com/callback') }
    }
}

# Simulates the propagation 404 that Graph returns immediately after New-MgApplication.
function Add-MgApplicationPassword {
    param($ApplicationId, $PasswordCredential, $ErrorAction)
    $script:passwordAttempts++
    if ($script:passwordPermanentFailure) {
        throw 'Insufficient privileges to complete the operation.'
    }
    if ($script:passwordAttempts -le $script:passwordFailuresBeforeSuccess) {
        throw "[Request_ResourceNotFound] : Resource '$ApplicationId' does not exist or one of its queried reference-property objects are not present."
    }
    [pscustomobject]@{ SecretText = 'client-secret' }
}

function New-MgServicePrincipal { param($AppId, $ErrorAction) [pscustomobject]@{ AppId = $AppId } }
function Start-Sleep { param($Seconds) $script:sleeps++ }

function Reset-Counters {
    $script:apiReads = 0
    $script:passwordAttempts = 0
    $script:sleeps = 0
    $script:apiMissesBeforeFound = 0
    $script:passwordFailuresBeforeSuccess = 0
    $script:passwordPermanentFailure = $false
}

# 1 - Transient API lookup miss and password propagation 404 are both retried, then succeed.
Reset-Counters
$script:apiMissesBeforeFound = 1
$script:passwordFailuresBeforeSuccess = 1
$client = New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client'
if ($client.AppId -ne 'client-app' -or $client.AppSecret -ne 'client-secret') {
    throw 'Client registration did not return the expected identity after retries.'
}
if ($script:apiReads -ne 2 -or $script:passwordAttempts -ne 2 -or $script:sleeps -ne 2) {
    throw "Transient Graph propagation errors were not retried exactly once each (apiReads=$script:apiReads passwordAttempts=$script:passwordAttempts sleeps=$script:sleeps)."
}

# 2 - A non-404 password error is surfaced immediately without retry.
Reset-Counters
$script:passwordPermanentFailure = $true
try {
    New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client' | Out-Null
    throw 'A non-propagation password error was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Insufficient privileges*' -or $script:passwordAttempts -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

# 3 - The password propagation 404 is retried a bounded number of times then propagated.
Reset-Counters
$script:passwordFailuresBeforeSuccess = 10
try {
    New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client' | Out-Null
    throw 'An unresolved propagation 404 was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Request_ResourceNotFound*' -or $script:passwordAttempts -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

# 4 - When the API application never becomes queryable, fail after bounded retries.
Reset-Counters
$script:apiMissesBeforeFound = 10
try {
    New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client' | Out-Null
    throw 'A never-propagating API application lookup was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*was not found on Microsoft Graph*' -or $script:apiReads -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

Write-Host 'Client registration retries Graph propagation for the API lookup and client secret.'
