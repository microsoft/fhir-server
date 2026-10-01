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

# Builds a structured Microsoft Graph ErrorRecord (an error body carrying a typed error code,
# as the Graph PowerShell SDK surfaces it) so the classifier can be exercised on the code
# itself rather than on free text. The human-readable message deliberately omits the error
# code so that only the structured code decides whether the read is retried. A real HTTP 404
# status is attached to the exception so that an unrelated code exercises a genuine
# "structured HTTP 404" - the case that must now fail fast rather than be retried on status.
function New-MgStructuredGraphError {
    param($Code, $ServicePrincipalId)
    $humanMessage = "Resource '$ServicePrincipalId' is not available right now."
    $body = [pscustomobject]@{ error = [pscustomobject]@{ code = $Code; message = $humanMessage } } | ConvertTo-Json -Compress
    $exception = [System.Exception]::new($humanMessage)
    $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 404 }) -Force
    $errorRecord = [System.Management.Automation.ErrorRecord]::new(
        $exception,
        "$Code,Microsoft.Graph.PowerShell.Cmdlets.GetMgServicePrincipalAppRoleAssignment",
        [System.Management.Automation.ErrorCategory]::ResourceUnavailable,
        $ServicePrincipalId)
    $errorRecord.ErrorDetails = [System.Management.Automation.ErrorDetails]::new($body)
    return $errorRecord
}

# Simulates Microsoft Graph eventual consistency for the appRoleAssignedTo relationship. The
# 1-based index of each read is matched against the configured read-index sets so a specific
# read (e.g. the initial read vs. the final verification read) can be made to fail:
#   $script:roleAssignmentOtherErrorReads - reads that fail with a non-404 error (fail fast)
#   $script:roleAssignment404Reads        - reads that fail with the transient 404 (retried)
#   $script:roleAssignmentStructuredReads - reads that fail with a structured error whose
#                                           typed code is $script:roleAssignmentStructuredCode
# Any read not in these sets returns the current assignments.
function Get-MgServicePrincipalAppRoleAssignment {
    param($ServicePrincipalId, $ErrorAction)
    $read = ++$script:roleAssignmentReads

    if ($read -in $script:roleAssignmentOtherErrorReads) {
        throw "Graph 403 Authorization_RequestDenied: Insufficient privileges to complete the operation."
    }

    if ($read -in $script:roleAssignment404Reads) {
        throw "Graph 404 Request_ResourceNotFound: Resource '$ServicePrincipalId' does not exist or one of its queried reference-property objects are not present."
    }

    if ($read -in $script:roleAssignmentStructuredReads) {
        throw (New-MgStructuredGraphError -Code $script:roleAssignmentStructuredCode -ServicePrincipalId $ServicePrincipalId)
    }

    # Microsoft Graph serves '/servicePrincipals/{id}/appRoleAssignments' as the assignments
    # whose PrincipalId is that service principal. Mirror that here so a read against the wrong
    # principal (e.g. the API/resource principal instead of the client) returns nothing - which
    # is exactly how the previous catch block masked a successful assignment as a failure.
    @($script:assignments | Where-Object { $_.PrincipalId -eq $ServicePrincipalId })
}

function New-MgServicePrincipalAppRoleAssignment {
    param($ServicePrincipalId, $PrincipalId, $ResourceId, $AppRoleId)
    # By default the create succeeds and the assignment becomes visible. Tests can configure
    # two Graph-realistic failure shapes via the script state:
    #   $script:newAssignmentError   - the error the create throws (Graph's known
    #                                  "reports failure" behaviour); $null means success.
    #   $script:newAssignmentApplies - when the create throws, whether Graph nonetheless
    #                                  persisted the assignment (the "reports failure but
    #                                  actually applied" case). When it stays $false the create
    #                                  throws without applying anything (a genuine failure).
    if (-not $script:newAssignmentError -or $script:newAssignmentApplies) {
        $script:assignments += [pscustomobject]@{
            Id          = 'assignment-id'
            PrincipalId = $PrincipalId
            ResourceId  = $ResourceId
            AppRoleId   = $AppRoleId
        }
    }

    if ($script:newAssignmentError) {
        throw $script:newAssignmentError
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
    $script:roleAssignment404Reads = @()
    $script:roleAssignmentOtherErrorReads = @()
    $script:roleAssignmentStructuredReads = @()
    $script:roleAssignmentStructuredCode = $null
    $script:newAssignmentError = $null
    $script:newAssignmentApplies = $false
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
$script:roleAssignment404Reads = @(1, 2)
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:sleeps -ne 2) {
    throw "A transient 404 on the assignments read was not retried exactly twice (sleeps=$script:sleeps)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw "A 404 retry must not create duplicate app role assignments (assignments=$(@($script:assignments).Count))."
}

# 7 - A non-404 error on the assignments read is not retried and surfaces immediately.
Reset-Counters
$script:roleAssignmentOtherErrorReads = @(1)
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
$script:roleAssignment404Reads = @(1, 2, 3, 4, 5)
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'A never-propagating 404 on the assignments read was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Request_ResourceNotFound*' -or $script:roleAssignmentReads -ne 5 -or $script:sleeps -ne 4) {
        throw
    }
}

# 9 - The initial assignments read succeeds, but the final verification read hits one transient
#     404: that read is wrapped in the same retry, so it recovers without a duplicate assignment.
Reset-Counters
$script:roleAssignment404Reads = @(2)
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:sleeps -ne 1 -or $script:roleAssignmentReads -ne 3) {
    throw "The final verification read did not retry the transient 404 exactly once (sleeps=$script:sleeps reads=$script:roleAssignmentReads)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw "Retrying the final verification read must not create duplicate app role assignments (assignments=$(@($script:assignments).Count))."
}

# 10 - A structured Graph 404 whose typed error code is Request_ResourceNotFound is retried
#      even when the error code never appears in the human-readable message.
Reset-Counters
$script:roleAssignmentStructuredReads = @(1)
$script:roleAssignmentStructuredCode = 'Request_ResourceNotFound'
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:sleeps -ne 1) {
    throw "A structured Request_ResourceNotFound was not retried exactly once (sleeps=$script:sleeps)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw 'The requested app role was not assigned after a structured Request_ResourceNotFound retry.'
}

# 11 - A structured HTTP 404 carrying an UNRELATED error code must fail fast: the classifier
#      trusts the typed code, so the read is not retried and the original error surfaces.
Reset-Counters
$script:roleAssignmentStructuredReads = @(1)
$script:roleAssignmentStructuredCode = 'Request_UnsupportedQuery'
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'An unrelated structured 404 on the assignments read was swallowed.'
}
catch {
    if ($_.FullyQualifiedErrorId -notlike 'Request_UnsupportedQuery*' -or $script:roleAssignmentReads -ne 1 -or $script:sleeps -ne 0) {
        throw
    }
}

# 12 - New-MgServicePrincipalAppRoleAssignment reports a failure but Microsoft Graph actually
#      applied the assignment: the catch verifies against the CLIENT service principal's own
#      assignments, finds the role, and swallows the spurious error instead of masking the
#      success. No duplicate assignment is created and no retry/sleep is needed.
Reset-Counters
$script:newAssignmentError = 'Graph reported a transient write failure for the app role assignment.'
$script:newAssignmentApplies = $true
Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
if ($script:sleeps -ne 0) {
    throw "A create that reported failure but actually applied must be confirmed on the first verification read (sleeps=$script:sleeps)."
}
if (@($script:assignments).Count -ne 1 -or $script:assignments[0].AppRoleId -ne 'role-guid') {
    throw "Verifying a spuriously-reported create failure must not create a duplicate assignment (assignments=$(@($script:assignments).Count))."
}

# 13 - New-MgServicePrincipalAppRoleAssignment fails for real (the assignment was NOT applied):
#      the verification read against the client service principal never finds the role, so the
#      ORIGINAL Graph error surfaces - not the old generic 'Failure adding app role assignment'
#      message - after the bounded verification retries are exhausted.
Reset-Counters
$script:newAssignmentError = 'Graph 403 Authorization_RequestDenied: Insufficient privileges to assign the app role.'
$script:newAssignmentApplies = $false
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'A real app role assignment failure was swallowed.'
}
catch {
    if ($_.Exception.Message -like '*Failure adding app role assignment*') {
        throw 'The original Graph error was masked by the generic failure message.'
    }
    if ($_.Exception.Message -notlike '*Authorization_RequestDenied*' -or $script:sleeps -ne 4) {
        throw
    }
    if (@($script:assignments).Count -ne 0) {
        throw "A real create failure must not leave an assignment behind (assignments=$(@($script:assignments).Count))."
    }
}

# 14 - The create fails for real AND the verification read ALSO fails (e.g. a 403 on the
#      probe). The secondary read failure must not replace the saved write error: the ORIGINAL
#      Graph write error is reported, and the probe failure is surfaced only as a warning.
Reset-Counters
$script:newAssignmentError = 'Graph 500 ServiceUnavailable: the app role assignment write failed.'
$script:newAssignmentApplies = $false
# Read 1 is the initial assignments read (must succeed so the role lands in rolesToAdd); the
# verification read inside the catch is read 2 and is forced to fail with a non-404 error.
$script:roleAssignmentOtherErrorReads = @(2)
try {
    Set-FhirServerClientAppRoleAssignments -ApiAppId 'api-app' -AppId 'client-app' -AppRoles @('globalAdmin')
    throw 'A real create failure with a failing verification read was swallowed.'
}
catch {
    if ($_.Exception.Message -like '*Authorization_RequestDenied*') {
        throw 'The verification read failure masked the original Graph write error.'
    }
    if ($_.Exception.Message -notlike '*ServiceUnavailable*' -or $_.Exception.Message -notlike '*write failed*') {
        throw
    }
    if (@($script:assignments).Count -ne 0) {
        throw "A real create failure must not leave an assignment behind (assignments=$(@($script:assignments).Count))."
    }
}

Write-Host 'Client app role assignment retries Graph propagation for the API and client service principals, retries the transient Request_ResourceNotFound while reading assignments (initial, catch, and final reads), fails fast on unrelated 404s, confirms a spuriously-reported create against the client principal, rethrows the original error on a real create failure, and preserves the original write error when the verification read itself fails.'
