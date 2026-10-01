# Regression coverage for Grant-ClientAppDelegatedPermissions.
#
# ADO build 51858 failed inside this function at Add-AadTestAuthEnvironment.ps1:277 with
# Set-StrictMode -Version Latest reporting "The property 'Id' cannot be found on this object."
# A task retry invoked the function right after creating the resource and client service
# principals, so Get-MgServicePrincipal had not yet observed the new principal(s) and the old
# code dereferenced .Id on $null. (Scenarios 1-4 below.)
#
# ADO build 51863 then failed in the same function while granting the delegated oauth2
# permissions: the first POST returned 404 Directory_ObjectNotFound ("Unable to read the company
# information from the directory") during principal propagation; the Azure DevOps task retry then
# got 400 Request_BadRequest "Permission entry already exists"; and the old fallback,
# Get-MgOauth2PermissionGrant, failed to load Microsoft.Graph.Identity.SignIns against an
# already-loaded Microsoft.Graph.Authentication v2.40.0. Scenarios 5-10 exercise the real grant
# call path with Microsoft Graph mocked to reproduce transient propagation, the idempotent
# "already exists" conflict (verified via Invoke-MgGraphRequest, including paging), verification
# that fails (which must re-throw the exact original write error), and a genuine non-retryable
# error. They also assert that Get-MgOauth2PermissionGrant is never invoked.

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'Private/Grant-ClientAppDelegatedPermissions.ps1')

# Microsoft Graph context used to derive the tenant id.
function Get-MgContext { [pscustomobject]@{ TenantId = 'test-tenant' } }

# Token acquisition and the two oauth2PermissionGrants POSTs all flow through Invoke-RestMethod.
# The token endpoint always returns a token-shaped object. Each oauth2PermissionGrants POST is
# driven per-scope by $script:grantPlan: an entry is either 'ok' (HTTP success) or a raw Graph
# error body string that is thrown to simulate an HTTP failure. With no plan entry the POST
# succeeds, which keeps scenarios 1-4 (service-principal propagation) unchanged.
function Invoke-RestMethod {
    param($Method, $Uri, $ContentType, $Body, $Headers, $ErrorVariable)

    if ($Uri -like '*oauth2/token') {
        return [pscustomobject]@{ access_token = 'test-token' }
    }

    $scope = ($Body | ConvertFrom-Json).scope
    if (-not $script:grantAttempts.ContainsKey($scope)) { $script:grantAttempts[$scope] = 0 }
    $script:grantAttempts[$scope]++
    $attempt = $script:grantAttempts[$scope]

    $outcome = 'ok'
    if ($script:grantPlan.ContainsKey($scope)) {
        $plan = @($script:grantPlan[$scope])
        if ($attempt -le $plan.Count) {
            $outcome = $plan[$attempt - 1]
        }
    }

    if ($outcome -eq 'ok') {
        return [pscustomobject]@{ id = "grant-$scope" }
    }

    # Simulate an Invoke-RestMethod HTTP failure carrying the Graph error envelope.
    throw $outcome
}

# Delegated-permission verification must go through Invoke-MgGraphRequest (part of the already
# loaded Microsoft.Graph.Authentication), paging via @odata.nextLink. Pages are served in order
# from $script:graphPages; an empty queue yields an empty result set. Every request URI is
# recorded so paging and the $filter can be asserted.
function Invoke-MgGraphRequest {
    param($Method, $Uri, $OutputType, $ErrorAction)
    $script:graphRequestUris += , $Uri
    if ($script:graphThrows) { throw 'Invoke-MgGraphRequest: failed to read the directory.' }
    $idx = $script:graphCall
    $script:graphCall++
    if ($idx -lt $script:graphPages.Count) {
        return $script:graphPages[$idx]
    }
    return @{ value = @() }
}

# Guard: the legacy fallback must never be called - it triggers the incompatible module import
# that broke build 51863.
function Get-MgOauth2PermissionGrant {
    param([switch]$All)
    $script:getMgOauthCalled = $true
    throw 'Get-MgOauth2PermissionGrant must not be called (triggers incompatible Graph module import).'
}

# Avoid real backoff delays; count the sleeps so retry behaviour can be asserted.
function Start-Sleep { param($Seconds) $script:sleeps++ }

$windowsAadId = "00000002-0000-0000-c000-000000000000"
$resourceAppId = 'resource-app'
$clientAppId = 'client-app'

# Scope names mirror the constants used inside Grant-ClientAppDelegatedPermissions.
$userReadScope = 'User.Read'
$userImpersonationScope = 'user_impersonation'

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
    $script:grantPlan = @{}
    $script:grantAttempts = @{}
    $script:graphPages = @()
    $script:graphCall = 0
    $script:graphRequestUris = @()
    $script:graphThrows = $false
    $script:getMgOauthCalled = $false
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

# Object ids the resolved service principals map to (Get-MgServicePrincipal returns Id = object-<appId>).
$windowsAadObjectId = "object-$windowsAadId"
$resourceObjectId = "object-$resourceAppId"

$directoryObjectNotFound = '{"error":{"code":"Directory_ObjectNotFound","message":"Unable to read the company information from the directory."}}'
$permissionAlreadyExists = '{"error":{"code":"Request_BadRequest","message":"Permission entry already exists."}}'
$insufficientPrivileges  = '{"error":{"code":"Authorization_RequestDenied","message":"Insufficient privileges to complete the operation."}}'

# 5 - TRANSIENT 404, WRITE DID NOT APPLY -> RETRY: the first User.Read POST returns 404
#     Directory_ObjectNotFound while the principals propagate. Before blindly reissuing the POST
#     (which in build 51863 produced a 400 "already exists" conflict), the function reconciles
#     read-only. The reconciliation reads come back empty/stale across the bounded, capped-backoff
#     reads, proving the write genuinely did not apply, so the POST is reissued and then succeeds -
#     without ever duplicating a grant. (RED on the pre-reconciliation code, which reissued the
#     POST immediately and performed zero verification reads.)
Reset-State
$script:grantPlan[$userReadScope] = @($directoryObjectNotFound, 'ok')
$script:graphPages = @()  # every reconciliation read is empty -> write did not apply
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:grantAttempts[$userReadScope] -ne 2) {
    throw "Transient 404 on the User.Read grant was not reissued exactly once after reconciliation (attempts=$($script:grantAttempts[$userReadScope]))."
}
if ($script:graphRequestUris.Count -ne 3) {
    throw "Reconciliation before the retry must perform the bounded three read-only probes (calls=$($script:graphRequestUris.Count))."
}
if ($script:graphRequestUris[0] -notlike "*oauth2PermissionGrants*clientId eq 'object-$clientAppId'*") {
    throw "Reconciliation did not filter oauth2PermissionGrants by the client object id. Got: $($script:graphRequestUris[0])"
}
if ($script:sleeps -ne 2) {
    throw "Expected exactly two capped reconciliation backoff sleeps before the retry (sleeps=$script:sleeps)."
}
if ($script:getMgOauthCalled) {
    throw 'Reconciliation used Get-MgOauth2PermissionGrant instead of Invoke-MgGraphRequest.'
}

# 6 - IDEMPOTENT CONFLICT: the task retry hits 400 "Permission entry already exists". Verification
#     via Invoke-MgGraphRequest confirms the grant, so the function succeeds without duplicating
#     the permission and without a second POST.
Reset-State
$script:grantPlan[$userReadScope] = @($permissionAlreadyExists)
$script:graphPages = @(
    @{ value = @( @{ resourceId = $windowsAadObjectId; scope = 'User.Read'; consentType = 'AllPrincipals' } ) }
)
$userReadUserUri = $null
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:grantAttempts[$userReadScope] -ne 1) {
    throw "An 'already exists' conflict must not re-POST the grant (attempts=$($script:grantAttempts[$userReadScope]))."
}
if ($script:graphRequestUris.Count -ne 1) {
    throw "Exactly one verification request expected for the single conflicting grant (calls=$($script:graphRequestUris.Count))."
}
if ($script:graphRequestUris[0] -notlike "*oauth2PermissionGrants*clientId eq 'object-$clientAppId'*") {
    throw "Verification did not filter oauth2PermissionGrants by the client object id. Got: $($script:graphRequestUris[0])"
}
if ($script:getMgOauthCalled) {
    throw 'Verification used Get-MgOauth2PermissionGrant instead of Invoke-MgGraphRequest.'
}

# 7 - IDEMPOTENT CONFLICT WITH PAGING: the matching grant is only on the second page (reached via
#     @odata.nextLink) and shares its scope string with another scope ("openid User.Read").
Reset-State
$script:grantPlan[$userReadScope] = @($permissionAlreadyExists)
$nextLink = 'https://graph.microsoft.com/v1.0/oauth2PermissionGrants?$skiptoken=page2'
$script:graphPages = @(
    @{ value = @( @{ resourceId = 'object-unrelated'; scope = 'Directory.Read.All'; consentType = 'AllPrincipals' } ); '@odata.nextLink' = $nextLink },
    @{ value = @( @{ resourceId = $windowsAadObjectId; scope = 'openid User.Read'; consentType = 'AllPrincipals' } ) }
)
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:graphRequestUris.Count -ne 2) {
    throw "Verification did not follow the @odata.nextLink page (calls=$($script:graphRequestUris.Count))."
}
if ($script:graphRequestUris[1] -ne $nextLink) {
    throw "The second verification request did not use the continuation link. Got: $($script:graphRequestUris[1])"
}

# 8 - CONFLICT BUT NOT PRESENT: the write fails with "already exists" yet verification finds no
#     such grant. The exact original write error must be re-thrown - no silent success.
Reset-State
$script:grantPlan[$userReadScope] = @($permissionAlreadyExists)
$script:graphPages = @()  # verification finds nothing
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'A conflicting write that cannot be verified was silently treated as success.'
}
catch {
    if ($_.Exception.Message -notlike '*Permission entry already exists*') {
        throw "The original write failure was not preserved. Got: $($_.Exception.Message)"
    }
}

# 9 - VERIFICATION FAILS: when Invoke-MgGraphRequest itself throws, the original write error (not
#     the verification error) must surface, so the real failure is never masked.
Reset-State
$script:grantPlan[$userReadScope] = @($permissionAlreadyExists)
$script:graphThrows = $true
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'A failed verification was treated as success.'
}
catch {
    if ($_.Exception.Message -notlike '*Permission entry already exists*') {
        throw "Verification failure masked the original write error. Got: $($_.Exception.Message)"
    }
}

# 10 - GENUINE NON-RETRYABLE ERROR: a 403-style failure is not retried; verification finds nothing
#      so the error surfaces immediately.
Reset-State
$script:grantPlan[$userReadScope] = @($insufficientPrivileges)
$script:graphPages = @()
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'A genuine non-retryable grant error was swallowed.'
}
catch {
    if ($_.Exception.Message -notlike '*Insufficient privileges*') {
        throw "A genuine grant error was not preserved. Got: $($_.Exception.Message)"
    }
    if ($script:grantAttempts[$userReadScope] -ne 1) {
        throw "A genuine non-retryable error must not be retried (attempts=$($script:grantAttempts[$userReadScope]))."
    }
    if ($script:sleeps -ne 0) {
        throw "A genuine non-retryable error must not back off (sleeps=$script:sleeps)."
    }
}

# 11 - RESOURCE (user_impersonation) PATH: the second grant likewise resolves an "already exists"
#      conflict idempotently against the resource application's service principal.
Reset-State
$script:grantPlan[$userImpersonationScope] = @($permissionAlreadyExists)
$script:graphPages = @(
    @{ value = @( @{ resourceId = $resourceObjectId; scope = 'user_impersonation'; consentType = 'AllPrincipals' } ) }
)
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:grantAttempts[$userImpersonationScope] -ne 1) {
    throw "The user_impersonation conflict must not re-POST (attempts=$($script:grantAttempts[$userImpersonationScope]))."
}
if ($script:graphRequestUris.Count -ne 1) {
    throw "Exactly one verification request expected for the user_impersonation grant (calls=$($script:graphRequestUris.Count))."
}
if ($script:getMgOauthCalled) {
    throw 'Verification used Get-MgOauth2PermissionGrant instead of Invoke-MgGraphRequest.'
}

# An error whose authoritative structured code is NOT the propagation code, yet whose free-text
# message happens to quote the propagation wording. The structured code must win.
$unrelatedCodeWithPropagationText = '{"error":{"code":"Request_BadRequest","message":"Unable to read the company information from the directory."}}'

# 12 - TRANSIENT 404, WRITE ALREADY APPLIED BUT INVISIBLE -> NO SECOND POST: the first User.Read
#      POST returns 404 Directory_ObjectNotFound, but the write actually applied; the grant is just
#      invisible to the list endpoint for the first two reconciliation reads and only becomes
#      visible on the third. Reconciliation observes it and returns success WITHOUT reissuing the
#      POST, so build 51863's duplicate/conflict is avoided entirely. (RED on the pre-reconciliation
#      code, which reissued the POST - grantAttempts would be 2 and no reads would occur.)
Reset-State
$script:grantPlan[$userReadScope] = @($directoryObjectNotFound)
$script:graphPages = @(
    @{ value = @() },                                                              # read 1: stale/empty
    @{ value = @() },                                                              # read 2: stale/empty
    @{ value = @( @{ resourceId = $windowsAadObjectId; scope = 'openid User.Read'; consentType = 'AllPrincipals' } ) }  # read 3: visible
)
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:grantAttempts[$userReadScope] -ne 1) {
    throw "A transient 404 whose write already applied must NOT be re-POSTed (attempts=$($script:grantAttempts[$userReadScope]))."
}
if ($script:graphRequestUris.Count -ne 3) {
    throw "Reconciliation should read until the grant becomes visible on the third probe (calls=$($script:graphRequestUris.Count))."
}
if ($script:sleeps -ne 2) {
    throw "Expected exactly two capped reconciliation backoff sleeps before the grant became visible (sleeps=$script:sleeps)."
}
if ($script:getMgOauthCalled) {
    throw 'Reconciliation used Get-MgOauth2PermissionGrant instead of Invoke-MgGraphRequest.'
}

# 13 - UNRELATED STRUCTURED CODE QUOTING PROPAGATION TEXT -> NOT RETRIED: the write fails with an
#      authoritative Request_BadRequest code whose message merely quotes the propagation wording.
#      The structured code must override the loose text, so this is treated as a non-retryable
#      error: it is verified once (finds nothing) and surfaces immediately - no reconciliation
#      retry loop. (RED on the loose OR classifier, which matched the text and retried the POST.)
Reset-State
$script:grantPlan[$userReadScope] = @($unrelatedCodeWithPropagationText)
$script:graphPages = @()  # verification finds nothing
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'An unrelated structured error quoting propagation text was mis-handled as success.'
}
catch {
    if ($_.Exception.Message -notlike '*Unable to read the company information from the directory*') {
        throw "The original write failure was not preserved. Got: $($_.Exception.Message)"
    }
    if ($script:grantAttempts[$userReadScope] -ne 1) {
        throw "An authoritative non-propagation code must not be retried (attempts=$($script:grantAttempts[$userReadScope]))."
    }
    if ($script:sleeps -ne 0) {
        throw "An authoritative non-propagation code must not back off (sleeps=$script:sleeps)."
    }
    if ($script:graphRequestUris.Count -ne 1) {
        throw "An authoritative non-propagation code takes the single-verification path, not reconciliation (calls=$($script:graphRequestUris.Count))."
    }
}

# 14 - PERSISTENT TRANSIENT 404 -> ORIGINAL WRITE ERROR PRESERVED: every POST returns 404
#      Directory_ObjectNotFound and the grant never becomes visible. Each attempt reconciles
#      read-only (never finding the grant) before reissuing, and when the bounded POST attempts are
#      exhausted the exact original write error is re-thrown - never masked by the empty reads.
#      (RED on the pre-reconciliation code, which performed only a single end-of-loop read.)
Reset-State
$script:grantPlan[$userReadScope] = @($directoryObjectNotFound, $directoryObjectNotFound, $directoryObjectNotFound, $directoryObjectNotFound, $directoryObjectNotFound)
$script:graphPages = @()  # grant never visible
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'A persistent transient failure was silently treated as success.'
}
catch {
    if ($_.Exception.Message -notlike '*Unable to read the company information from the directory*') {
        throw "The persistent transient failure did not preserve the original write error. Got: $($_.Exception.Message)"
    }
    if ($script:grantAttempts[$userReadScope] -ne 5) {
        throw "Transient retries must be bounded to five POST attempts (attempts=$($script:grantAttempts[$userReadScope]))."
    }
    if ($script:graphRequestUris.Count -lt 5) {
        throw "Reconciliation must read before each reissue, not just once at the end (calls=$($script:graphRequestUris.Count))."
    }
}

# 15 - TRANSIENT 404 THEN DUPLICATE CONFLICT, WRITE VISIBLE AFTER >3 EMPTY READS -> NO FURTHER POST:
#      the first User.Read POST returns 404 Directory_ObjectNotFound. Reconciliation reads three
#      empty/stale pages, proving (within that bounded window) the grant is absent, so the POST is
#      reissued. That reissued POST now hits the exact build 51863 race: 400 "Permission entry
#      already exists", because the first write HAD applied but was still invisible. The conflict is
#      resolved by the SAME bounded delayed read-only reconciliation; the grant finally surfaces on
#      the third reconciliation read (the sixth overall, i.e. after more than three empty reads), so
#      the function succeeds WITHOUT issuing a third POST and without duplicating the grant. (RED on
#      code that gave the conflict only a single verification read: that read is still empty, so the
#      original 400 would be re-thrown as a spurious failure.)
Reset-State
$script:grantPlan[$userReadScope] = @($directoryObjectNotFound, $permissionAlreadyExists)
$script:graphPages = @(
    @{ value = @() },  # reconcile-before-retry read 1: empty
    @{ value = @() },  # reconcile-before-retry read 2: empty
    @{ value = @() },  # reconcile-before-retry read 3: empty -> reissue POST
    @{ value = @() },  # post-conflict reconcile read 1: still invisible
    @{ value = @() },  # post-conflict reconcile read 2: still invisible
    @{ value = @( @{ resourceId = $windowsAadObjectId; scope = 'openid User.Read'; consentType = 'AllPrincipals' } ) }  # read 3: finally visible
)
Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
if ($script:grantAttempts[$userReadScope] -ne 2) {
    throw "A duplicate-conflict after one reconciled retry must NOT issue a third POST (attempts=$($script:grantAttempts[$userReadScope]))."
}
if ($script:graphRequestUris.Count -ne 6) {
    throw "Expected three reconcile-before-retry reads plus three post-conflict reconciliation reads (calls=$($script:graphRequestUris.Count))."
}
if ($script:getMgOauthCalled) {
    throw 'Conflict reconciliation used Get-MgOauth2PermissionGrant instead of Invoke-MgGraphRequest.'
}

# 16 - CONSENT TYPE MUST MATCH: a delegated grant for the same clientId, resourceId and scope exists
#      but with consentType 'Principal' (a single user's own consent for one principalId), NOT the
#      'AllPrincipals' tenant-wide admin consent this function grants. The "already exists" conflict
#      must therefore NOT be treated as satisfied by that per-user grant - verification requires the
#      consentType to match - so the exact original write error surfaces rather than a false success.
#      (RED on code that matched only resourceId + scope: it would mistake the Principal consent for
#      the AllPrincipals grant and silently swallow the failure.)
Reset-State
$script:grantPlan[$userReadScope] = @($permissionAlreadyExists)
$principalConsentGrant = @{ resourceId = $windowsAadObjectId; scope = 'User.Read'; consentType = 'Principal' }
$script:graphPages = @(
    @{ value = @( $principalConsentGrant ) },
    @{ value = @( $principalConsentGrant ) },
    @{ value = @( $principalConsentGrant ) }
)
try {
    Grant-ClientAppDelegatedPermissions @invokeArgs | Out-Null
    throw 'A per-user (Principal) consent was wrongly accepted as the AllPrincipals grant.'
}
catch {
    if ($_.Exception.Message -notlike '*Permission entry already exists*') {
        throw "A Principal-consent-only match did not preserve the original write error. Got: $($_.Exception.Message)"
    }
    if ($script:grantAttempts[$userReadScope] -ne 1) {
        throw "An 'already exists' conflict must not re-POST even when only a non-matching consentType exists (attempts=$($script:grantAttempts[$userReadScope]))."
    }
    if ($script:getMgOauthCalled) {
        throw 'Verification used Get-MgOauth2PermissionGrant instead of Invoke-MgGraphRequest.'
    }
}

Write-Host 'Grant-ClientAppDelegatedPermissions retries delayed service principal propagation and transient directory errors, resolves "already exists" conflicts idempotently via Invoke-MgGraphRequest (with paging), preserves the exact original write error when verification fails, and never calls Get-MgOauth2PermissionGrant.'
