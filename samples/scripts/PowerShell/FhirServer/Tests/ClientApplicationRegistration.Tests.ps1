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

# Builds an ErrorRecord that mirrors how Microsoft Graph surfaces an error: the typed code
# can appear in the structured response body (ErrorDetails.Message JSON), in the
# FullyQualifiedErrorId, and/or in the plain exception message. Each channel is set
# independently so classification can be tested when the code is present in only one of them.
function New-GraphErrorRecord {
    param(
        [string]$ExceptionMessage = 'The remote server returned an error.',
        [string]$FullyQualifiedErrorId = 'GenericFailure,Microsoft.Graph.PowerShell.Cmdlets',
        [string]$ErrorDetailsJson
    )
    $exception = [System.Exception]::new($ExceptionMessage)
    $record = [System.Management.Automation.ErrorRecord]::new(
        $exception,
        $FullyQualifiedErrorId,
        [System.Management.Automation.ErrorCategory]::InvalidOperation,
        $null)
    if ($ErrorDetailsJson) {
        $record.ErrorDetails = [System.Management.Automation.ErrorDetails]::new($ErrorDetailsJson)
    }
    $record
}

# Simulates the propagation 404 that Graph returns immediately after New-MgApplication.
# The transient/permanent errors are configurable (plain string by default, or a full
# ErrorRecord via $script:passwordTransientRecord / $script:passwordPermanentRecord) so
# both observed read-after-write 404s (Request_ResourceNotFound and Directory_ObjectNotFound)
# can be exercised through each error channel, as well as unrelated failures that must fail fast.
function Add-MgApplicationPassword {
    param($ApplicationId, $PasswordCredential, $ErrorAction)
    $script:passwordAttempts++
    if ($script:passwordPermanentFailure) {
        if ($script:passwordPermanentRecord) {
            throw $script:passwordPermanentRecord
        }
        throw $script:passwordPermanentError
    }
    if ($script:passwordAttempts -le $script:passwordFailuresBeforeSuccess) {
        if ($script:passwordTransientRecord) {
            throw $script:passwordTransientRecord
        }
        throw ($script:passwordTransientError.Replace('{0}', [string]$ApplicationId))
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
    # Default transient 404 is the application-object propagation error.
    $script:passwordTransientError = "[Request_ResourceNotFound] : Resource '{0}' does not exist or one of its queried reference-property objects are not present."
    # Default permanent error is a non-404 authorization failure.
    $script:passwordPermanentError = 'Insufficient privileges to complete the operation.'
    # ErrorRecord overrides (take precedence over the string messages above when set).
    $script:passwordTransientRecord = $null
    $script:passwordPermanentRecord = $null
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

# 5 - The Directory_ObjectNotFound 404 ("Unable to read the company information from the
#     directory", observed in ADO build 51862) is treated as transient and retried, then
#     succeeds. This is the regression case for the read-after-write secret creation path.
Reset-Counters
$script:passwordTransientError = '[Directory_ObjectNotFound] : Unable to read the company information from the directory.'
$script:passwordFailuresBeforeSuccess = 1
$client = New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client'
if ($client.AppSecret -ne 'client-secret') {
    throw 'Directory_ObjectNotFound propagation 404 was not retried to success.'
}
if ($script:passwordAttempts -ne 2 -or $script:sleeps -ne 1) {
    throw "Directory_ObjectNotFound 404 was not retried exactly once (passwordAttempts=$script:passwordAttempts sleeps=$script:sleeps)."
}

# 6 - A Directory_ObjectNotFound 404 that never clears is retried a bounded number of
#     times then surfaced as a terminal error.
Reset-Counters
$script:passwordTransientError = '[Directory_ObjectNotFound] : Unable to read the company information from the directory.'
$script:passwordFailuresBeforeSuccess = 10
try {
    New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client' | Out-Null
    throw 'An unresolved Directory_ObjectNotFound 404 was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Directory_ObjectNotFound*' -or $script:passwordAttempts -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

# 7 - An unrelated 404 (not one of the known propagation codes) must fail fast and never
#     be retried, so broad "not found"/404 text matching cannot mask permanent errors.
Reset-Counters
$script:passwordPermanentFailure = $true
$script:passwordPermanentError = 'Response status code does not indicate success: 404 (Not Found).'
try {
    New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client' | Out-Null
    throw 'An unrelated 404 password error was retried instead of failing fast.'
}
catch {
    if ($_.Exception.Message -notlike '*404 (Not Found)*' -or $script:passwordAttempts -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

# 8 - The Graph code appears ONLY in the structured response body (ErrorDetails.Message
#     JSON); the exception message and FullyQualifiedErrorId carry no code. The structured
#     body is authoritative, so this is classified transient and retried to success.
Reset-Counters
$script:passwordFailuresBeforeSuccess = 1
$script:passwordTransientRecord = New-GraphErrorRecord `
    -ExceptionMessage 'The remote server returned an error: (404) Not Found.' `
    -FullyQualifiedErrorId 'GenericFailure,Microsoft.Graph.PowerShell.Cmdlets' `
    -ErrorDetailsJson '{"error":{"code":"Directory_ObjectNotFound","message":"Unable to read the company information from the directory."}}'
$client = New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client'
if ($client.AppSecret -ne 'client-secret') {
    throw 'Directory_ObjectNotFound code carried only in the structured body was not retried to success.'
}
if ($script:passwordAttempts -ne 2 -or $script:sleeps -ne 1) {
    throw "Structured-body Directory_ObjectNotFound was not retried exactly once (passwordAttempts=$script:passwordAttempts sleeps=$script:sleeps)."
}

# 9 - The Graph code appears ONLY in the FullyQualifiedErrorId; the exception message has
#     no code and there is no structured body. It is still classified transient and retried.
Reset-Counters
$script:passwordFailuresBeforeSuccess = 1
$script:passwordTransientRecord = New-GraphErrorRecord `
    -ExceptionMessage 'The remote server returned an error: (404) Not Found.' `
    -FullyQualifiedErrorId 'Request_ResourceNotFound,Microsoft.Graph.PowerShell.Cmdlets.AddMgApplicationPassword_Create'
$client = New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client'
if ($client.AppSecret -ne 'client-secret') {
    throw 'Request_ResourceNotFound code carried only in the FullyQualifiedErrorId was not retried to success.'
}
if ($script:passwordAttempts -ne 2 -or $script:sleeps -ne 1) {
    throw "FullyQualifiedErrorId Request_ResourceNotFound was not retried exactly once (passwordAttempts=$script:passwordAttempts sleeps=$script:sleeps)."
}

# 10 - An unrelated structured 404 (authoritative code is NOT a known propagation code)
#      must fail fast and never be retried, even though its exception/body text contains
#      "404"/"not found". This proves the structured code guards against masking.
Reset-Counters
$script:passwordPermanentFailure = $true
$script:passwordPermanentRecord = New-GraphErrorRecord `
    -ExceptionMessage 'The remote server returned an error: (404) Not Found.' `
    -FullyQualifiedErrorId 'Request_BadRequest,Microsoft.Graph.PowerShell.Cmdlets.AddMgApplicationPassword_Create' `
    -ErrorDetailsJson '{"error":{"code":"Request_BadRequest","message":"The directory object cannot be found (404)."}}'
try {
    New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client' | Out-Null
    throw 'An unrelated structured 404 error was retried instead of failing fast.'
}
catch {
    if ($_.Exception.Message -notlike '*404*' -or $script:passwordAttempts -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

Write-Host 'Client registration retries Graph propagation for the API lookup and client secret.'
