. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Public/Set-FhirServerApiApplicationRoles.ps1')

function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }
function Get-MgApplication {
    $script:reads++
    [pscustomobject]@{
        Id = 'application-object'
        AppRoles = @([pscustomobject]@{
            Id = 'old-role'
            Value = 'admin'
            IsEnabled = ($script:neverPropagate -or -not $script:disableWritten -or $script:reads -le 2)
        })
    }
}
function Update-MgApplication {
    param($ApplicationId, $AppRoles, $ErrorAction)
    if ($AppRoles.Count -eq 1 -and $AppRoles[0].Value -eq 'admin') {
        $script:disableWritten = $true
        return
    }
    if ($script:reads -le 2) {
        throw 'Permission (scope or role) cannot be deleted or updated unless disabled first. CannotDeleteOrUpdateEnabledEntitlement'
    }
    if ($script:graphErrorAttempts -gt 0) {
        $script:graphErrorAttempts--
        throw 'CannotDeleteOrUpdateEnabledEntitlement: Permission (scope or role) cannot be deleted or updated unless disabled first.'
    }
    $script:rolesUpdated = $true
}
function Start-Sleep { param($Seconds) $script:sleeps++ }

$script:reads = 0
$script:sleeps = 0
$script:disableWritten = $false
$script:rolesUpdated = $false
$script:neverPropagate = $false
$script:graphErrorAttempts = 0
Set-FhirServerApiApplicationRoles -ApiAppId 'api-app' -AppRoles @('reader')
if (-not $script:rolesUpdated -or -not $script:disableWritten -or $script:sleeps -lt 1) {
    throw 'App roles were not safely replaced after disabling the old role.'
}

$script:reads = 0
$script:sleeps = 0
$script:disableWritten = $false
$script:rolesUpdated = $false
$script:graphErrorAttempts = 1
Set-FhirServerApiApplicationRoles -ApiAppId 'api-app' -AppRoles @('reader')
if (-not $script:rolesUpdated -or $script:graphErrorAttempts -ne 0 -or $script:sleeps -ne 2) {
    throw 'A transient Graph entitlement error was not retried after the roles became disabled.'
}

$script:reads = 0
$script:sleeps = 0
$script:disableWritten = $false
$script:rolesUpdated = $false
$script:neverPropagate = $true
try {
    Set-FhirServerApiApplicationRoles -ApiAppId 'api-app' -AppRoles @('reader')
    throw 'Role propagation timeout was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*did not become disabled*' -or $script:sleeps -ne 4 -or $script:rolesUpdated) {
        throw
    }
}

Write-Host 'Application roles updated after old roles became disabled.'
