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

# Simulates Microsoft Graph eventual consistency for the appRoleAssignedTo relationship of
# a freshly created service principal: the first $script:roleAssignmentOtherErrorMisses reads
# fail with a non-404 error (which must surface immediately), the next
# $script:roleAssignment404Misses reads fail with the transient 404 Request_ResourceNotFound
# (which must be retried), and every read after that returns the current assignments.
function Get-MgServicePrincipalAppRoleAssignment {
    param($ServicePrincipalId, $ErrorAction)
    $script:roleAssignmentReads++

    if ($script:roleAssignmentReads -le $script:roleAssignmentOtherErrorMisses) {
        throw "Graph 403 Authorization_RequestDenied: Insufficient privileges to complete the operation."
    }

    if ($script:roleAssignmentReads -le ($script:roleAssignmentOtherErrorMisses + $script:roleAssignment404Misses)) {
        throw "Graph 404 Request_ResourceNotFound: Resource '$ServicePrincipalId' does not exist or one of its queried reference-property objects are not present."
    }

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
    $script:roleAssignmentReads = 0
    $script:roleAssignment404Misses = 0
    $script:roleAssignmentOtherErrorMisses = 0
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

# 5 - The appRoleAssignedTo read answers immediately with no assignments (an empty result is
#     a valid answer, not an error): the requested role is assigned without any 404 retry.
Reset-Counters
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:sleeps -ne 0) {
    throw "An empty appRoleAssignedTo result must not trigger a 404 retry (sleeps=$script:sleeps)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw 'The requested app role was not assigned when the initial assignments read was empty.'
}

# 6 - The appRoleAssignedTo relationship propagates after two transient 404s: the read is
#     retried exactly twice and the role is still assigned exactly once (no duplicate creation).
Reset-Counters
$script:roleAssignment404Misses = 2
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:sleeps -ne 2) {
    throw "A transient 404 on the assignments read was not retried exactly twice (sleeps=$script:sleeps)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw "A 404 retry must not create duplicate app role assignments (assignments=$(@($script:assignments).Count))."
}

# 7 - A non-404 error on the assignments read is not retried and surfaces immediately.
Reset-Counters
$script:roleAssignmentOtherErrorMisses = 1
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'A non-404 error on the assignments read was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Authorization_RequestDenied*' -or $script:roleAssignmentReads -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

# 8 - A 404 that never clears fails after bounded retries and propagates the original error.
Reset-Counters
$script:roleAssignment404Misses = 10
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'A never-propagating 404 on the assignments read was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Request_ResourceNotFound*' -or $script:roleAssignmentReads -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

Write-Host 'Client app role assignment retries Graph propagation for the API and client service principals, and retries the transient 404 while reading existing assignments.'
