# Regression coverage for Grant-ClientAppDelegatedPermissions service principal lookups.
#
# ADO build 51858 failed inside this function at Add-AadTestAuthEnvironment.ps1:277 with
# Set-StrictMode -Version Latest reporting "The property 'Id' cannot be found on this object."
# A task retry invoked the function right after creating the resource and client service
# principals, so Get-MgServicePrincipal had not yet observed the new principal(s) and the old
# code dereferenced .Id on $null. These tests exercise the real function at its true call site
# (direct invocation of Grant-ClientAppDelegatedPermissions) with Microsoft Graph mocked to
# reproduce propagation delay, exhaustion, and genuine Graph errors.

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Private/Grant-ClientAppDelegatedPermissions.ps1')

# Microsoft Graph context used to derive the tenant id.
function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }

# Token acquisition and the two oauth2PermissionGrants POSTs all flow through Invoke-RestMethod.
# Returning a token-shaped object keeps the happy-path lookups as the only variable under test.
function Invoke-RestMethod {
    param($Method, $Uri, $ContentType, $Body, $Headers, $ErrorVariable)
    [pscustomobject]@{ access_token = 'test-token' }
}

# Avoid real backoff delays; count the sleeps so retry behaviour can be asserted.
function Start-Sleep { param($Seconds) $script:sleeps++ }

$windowsAadId = "00000002-0000-0000-c000-000000000000"
$resourceAppId = 'resource-app'
$clientAppId = 'client-app'

# $script:visibleAfter maps a filtered appId to the attempt number on which Graph first returns
# the principal. $script:graphError, when set, makes every lookup throw a genuine Graph failure.
function Get-MgServicePrincipal {
    param($Filter, $ErrorAction)

    if ($script:graphError) {
        throw 'Get-MgServicePrincipal: Insufficient privileges to complete the operation.'
    }

    $appId = ($Filter -replace ".*appId eq '", '') -replace "'.*", ''

    if (-not $script:attemptsByApp.ContainsKey($appId)) {
        $script:attemptsByApp[$appId] = 0
    }
    $script:attemptsByApp[$appId]++

    $readyOn = $script:visibleAfter[$appId]
    if ($null -ne $readyOn -and $script:attemptsByApp[$appId] -ge $readyOn) {
        return [pscustomobject]@{ Id = "object-$appId" }
    }

    # Propagation delay: Graph returns nothing for a principal it has not yet observed.
    return $null
}

$credential = New-Object pscredential('tenant-admin', (ConvertTo-SecureString 'tenant-secret' -AsPlainText -Force))

function Reset-State {
    $script:sleeps = 0
    $script:graphError = $false
    $script:attemptsByApp = @{}
    # By default every principal is immediately visible.
    $script:visibleAfter = @{
        $windowsAadId  = 1
        $resourceAppId = 1
        $clientAppId   = 1
    }
}

$invokeArgs = @{
    AppId                 = $clientAppId
    TenantAdminCredential = $credential
    ResourceApplicationId = $resourceAppId
}

# 1 - GREEN: a delayed client principal that appears on the third lookup is retried and resolved.
Reset-State
$script:visibleAfter[$clientAppId] = 3
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:attemptsByApp[$clientAppId] -ne 3) {
    throw "Delayed client principal was not retried until visible (clientAttempts=$($script:attemptsByApp[$clientAppId]))."
}
if ($script:sleeps -ne 2) {
    throw "Expected exactly two backoff sleeps while waiting for the client principal (sleeps=$script:sleeps)."
}

# 2 - GREEN: a delayed resource principal is likewise retried then resolved.
Reset-State
$script:visibleAfter[$resourceAppId] = 2
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:attemptsByApp[$resourceAppId] -ne 2 -or $script:sleeps -ne 1) {
    throw "Delayed resource principal retry was incorrect (resourceAttempts=$($script:attemptsByApp[$resourceAppId]) sleeps=$script:sleeps)."
}

# 3 - RED->explicit failure: a principal that never propagates fails after bounded retries with a
# descriptive message, not the opaque StrictMode "property 'Id' cannot be found" error.
Reset-State
$script:visibleAfter[$clientAppId] = 999  # never becomes visible
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'An unresolved service principal lookup was swallowed instead of failing.'
}
catch {
    if ($_.Exception.Message -notlike "*Unable to resolve the service principal Id for client application $clientAppId*") {
        throw "Exhaustion did not surface the descriptive failure. Got: $($_.Exception.Message)"
    }
    if ($script:attemptsByApp[$clientAppId] -ne 5 -or $script:sleeps -ne 4) {
        throw "Exhaustion was not bounded to five attempts (clientAttempts=$($script:attemptsByApp[$clientAppId]) sleeps=$script:sleeps)."
    }
}

# 4 - FAIL FAST: a genuine Graph error surfaces immediately without retry or being masked.
Reset-State
$script:graphError = $true
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'A genuine Graph error was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Insufficient privileges*') {
        throw "A genuine Graph error was not preserved. Got: $($_.Exception.Message)"
    }
    if ($script:sleeps -ne 0) {
        throw "A genuine Graph error must not be retried (sleeps=$script:sleeps)."
    }
}

Write-Host 'Grant-ClientAppDelegatedPermissions retries delayed service principal propagation, fails explicitly on exhaustion, and preserves genuine Graph errors.'
