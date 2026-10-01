$moduleRoot = Split-Path -Parent $PSScriptRoot
. "$moduleRoot/Public/Set-FhirServerClientAppRoleAssignments.ps1"

function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }

# Simulates Microsoft Graph eventual consistency: the API and client service principals
# are not queryable for the first $script:apiMisses / $script:clientMisses reads after
# they are created. The two lookups are told apart by the appId embedded in the filter.
function Get-MgServicePrincipal {
    param($Filter, $ErrorAction)
    if ($Filter -like "*'api-app'*") {
        $script:apiReads++
        if ($script:apiReads -le $script:apiMisses) { return $null }
        return [pscustomobject]@{
            Id       = 'api-object'
            AppRoles = @([pscustomobject]@{ Id = 'role-guid'; Value = 'globalAdmin' })
        }
    }

    $script:clientReads++
    if ($script:clientReads -le $script:clientMisses) { return $null }
    [pscustomobject]@{ Id = 'client-object' }
}

function Get-MgServicePrincipalAppRoleAssignment {
    param($ServicePrincipalId)
    $script:assignments
}

function New-MgServicePrincipalAppRoleAssignment {
    param($ServicePrincipalId, $PrincipalId, $ResourceId, $AppRoleId)
    $script:assignments += [pscustomobject]@{
        Id          = 'assignment-id'
        PrincipalId = $PrincipalId
        ResourceId  = $ResourceId
        AppRoleId   = $AppRoleId
    }
}

function Remove-MgServicePrincipalAppRoleAssignment { param($ServicePrincipalId, $AppRoleAssignmentId) }
function Start-Sleep { param($Seconds) $script:sleeps++ }

function Reset-Counters {
    $script:apiReads = 0
    $script:clientReads = 0
    $script:sleeps = 0
    $script:apiMisses = 0
    $script:clientMisses = 0
    $script:assignments = @()
}

# 1 - Both service principals are queryable immediately: no retries, roles are assigned.
Reset-Counters
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:apiReads -ne 1 -or $script:clientReads -ne 1 -or $script:sleeps -ne 0) {
    throw "The happy path must not retry the service principal lookups (apiReads=$script:apiReads clientReads=$script:clientReads sleeps=$script:sleeps)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw 'The requested app role was not assigned on the happy path.'
}

# 2 - The client service principal propagates after two misses: the lookup is retried.
Reset-Counters
$script:clientMisses = 2
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:clientReads -ne 3 -or $script:sleeps -ne 2) {
    throw "A propagating client service principal was not retried exactly twice (clientReads=$script:clientReads sleeps=$script:sleeps)."
}

# 3 - The API service principal propagates after two misses: the lookup is retried.
Reset-Counters
$script:apiMisses = 2
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:apiReads -ne 3 -or $script:sleeps -ne 2) {
    throw "A propagating API service principal was not retried exactly twice (apiReads=$script:apiReads sleeps=$script:sleeps)."
}

# 4 - A client service principal that never propagates fails explicitly after bounded retries.
Reset-Counters
$script:clientMisses = 10
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'A never-propagating client service principal lookup was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*was not found on Microsoft Graph*' -or $script:clientReads -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

Write-Host 'Client app role assignment retries Graph propagation for the API and client service principals.'
