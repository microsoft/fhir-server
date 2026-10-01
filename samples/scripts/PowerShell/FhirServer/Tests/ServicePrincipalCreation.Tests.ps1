$moduleRoot = Split-Path -Parent $PSScriptRoot
Get-ChildItem "$moduleRoot/Private/*.ps1" | ForEach-Object { . $_.FullName }
. "$moduleRoot/Public/New-FhirServerApiApplicationRegistration.ps1"
. "$moduleRoot/Public/New-FhirServerClientApplicationRegistration.ps1"

function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }
function Get-MgApplication { [pscustomobject]@{ AppId = 'api-app'; IdentifierUris = @('api://test'); Api = $null } }
function New-MgApplication {
    param($DisplayName)
    [pscustomobject]@{
        AppId = if ($DisplayName -eq 'api://test') { 'api-app' } else { 'client-app' }
        Id = 'application-object'
        Web = [pscustomobject]@{ RedirectUris = @('https://example.com/callback') }
    }
}
function Add-MgApplicationPassword { [pscustomobject]@{ SecretText = 'test-secret' } }
function Start-Sleep { param($Seconds) $script:sleeps++ }
function New-MgServicePrincipal {
    param($AppId, $ErrorAction)
    $script:attempts++
    if ($script:permanentFailure) {
        throw 'Authorization_RequestDenied'
    }
    if ($script:attempts -le $script:failuresBeforeSuccess) {
        throw "The appId '$AppId' of the service principal does not reference a valid application object."
    }
    [pscustomobject]@{ AppId = $AppId }
}

$script:attempts = 0
$script:sleeps = 0
$script:failuresBeforeSuccess = 1
$script:permanentFailure = $false
$api = New-FhirServerApiApplicationRegistration -FhirServiceAudience 'api://test'
if ($api.AppId -ne 'api-app' -or $script:attempts -ne 2 -or $script:sleeps -ne 1) {
    throw 'API registration did not retry the transient Graph error.'
}

$script:attempts = 0
$script:sleeps = 0
$client = New-FhirServerClientApplicationRegistration -ApiAppId 'api-app' -DisplayName 'test-client'
if ($client.AppId -ne 'client-app' -or $script:attempts -ne 2 -or $script:sleeps -ne 1) {
    throw 'Client registration did not retry the transient Graph error.'
}

$script:attempts = 0
$script:sleeps = 0
$script:permanentFailure = $true
try {
    New-FhirServerApiApplicationRegistration -FhirServiceAudience 'api://test' | Out-Null
    throw 'Permanent Graph error was swallowed.'
}
catch {
    if ($_.Exception.Message -ne 'Authorization_RequestDenied' -or $script:attempts -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

$script:attempts = 0
$script:sleeps = 0
$script:permanentFailure = $false
$script:failuresBeforeSuccess = 5
try {
    New-FhirServerApiApplicationRegistration -FhirServiceAudience 'api://test' | Out-Null
    throw 'Transient Graph error was swallowed after exhausting retries.'
}
catch {
    if ($_.Exception.Message -notlike '*does not reference a valid application object*' -or $script:attempts -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

Write-Host 'Service principal creation retries transient failures and propagates permanent failures.'
