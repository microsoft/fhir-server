function Grant-ClientAppDelegatedPermissions {
    <#
    .SYNOPSIS
    Grants delegated permissions to a client app, so that users of the app are 
    not required to consent to the app calling the FHIR apli app on their behalf.
    .PARAMETER AppId
    The client application app ID.
    .PARAMETER TenantAdminCredential
    Credentials for a tenant admin user
    .PARAMETER ResourceApplicationId
    Application Id for the resource for which we need access
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AppId,

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [pscredential]$TenantAdminCredential,

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [string]$ResourceApplicationId
    )

    Set-StrictMode -Version Latest

    Write-Host "Granting delegated permissions for app ID $AppId"

    # A task retry can call this function immediately after the resource and client service
    # principals are created. Microsoft Graph may not have finished propagating a brand new
    # principal, so Get-MgServicePrincipal returns nothing and the previous code then accessed
    # .Id on $null, which under Set-StrictMode -Version Latest fails with
    # "The property 'Id' cannot be found on this object." Retry the lookup (only when it returns
    # nothing) with bounded backoff, let any genuine Graph error surface immediately via
    # -ErrorAction Stop, and fail explicitly if the principal never becomes visible.
    function Resolve-ServicePrincipalId {
        param(
            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$Filter,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$Description,

            [int]$MaxAttempts = 5
        )

        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            # Do not wrap this in a catch: a real Graph failure must propagate unchanged.
            $servicePrincipal = Get-MgServicePrincipal -Filter $Filter -ErrorAction Stop

            if ($servicePrincipal) {
                $servicePrincipal = @($servicePrincipal)[0]
                if ($servicePrincipal.Id) {
                    return $servicePrincipal.Id
                }
            }

            if ($attempt -lt $MaxAttempts) {
                Write-Warning "Service principal for $Description not yet visible in Microsoft Graph (attempt $attempt of $MaxAttempts). Retrying after backoff."
                Start-Sleep -Seconds (5 * [math]::Pow(2, $attempt - 1))
            }
        }

        throw "Unable to resolve the service principal Id for $Description after $MaxAttempts attempts. The principal may not have finished propagating in Microsoft Graph."
    }

    # Get token to talk to graph api using Microsoft Graph context
    $context = Get-MgContext
    if (-not $context) {
        throw "No Microsoft Graph context found. Please connect using Connect-MgGraph first."
    }
    
    $tenantId = $context.TenantId

    $adTokenUrl = "https://login.microsoftonline.com/$tenantId/oauth2/token"
    $resource = "https://graph.microsoft.com/"

    $body = @{
        grant_type = "client_credentials"
        client_id  = $TenantAdminCredential.GetNetworkCredential().UserName
        client_secret = $TenantAdminCredential.GetNetworkCredential().Password
        resource   = $resource
    }

    try {
        $response = Invoke-RestMethod -Method 'Post' -Uri $adTokenUrl -ContentType "application/x-www-form-urlencoded" -Body $body -ErrorVariable error
    }
    catch {
        Write-Warning "Failed to get auth token to talk to graph api."
        Write-Warning "Error message: $error"

        throw
    }

    $windowsAadId = "00000002-0000-0000-c000-000000000000"  #ResourceId for Windows Azure Active Directory
    $windowsAadObjectId = Resolve-ServicePrincipalId -Filter "appId eq '$windowsAadId'" -Description "Windows Azure Active Directory ($windowsAadId)"

    $resourceApiObjectId = Resolve-ServicePrincipalId -Filter "appId eq '$ResourceApplicationId'" -Description "resource application $ResourceApplicationId"

    $clientObjectId = Resolve-ServicePrincipalId -Filter "appId eq '$AppId'" -Description "client application $AppId"

    $header = @{
        'Authorization' = 'Bearer ' + $response.access_token
        'Content-Type' = 'application/json'
    }

    $userReadScope = "User.Read"
    $userImpersonationScope = "user_impersonation"

    $permissionGrantUrl = "https://graph.microsoft.com/v1.0/oauth2PermissionGrants"

    # Extract the Microsoft Graph error code/message from a failed Invoke-RestMethod call so that a
    # write failure can be classified precisely. In PowerShell Core the HTTP response body - which
    # carries the Graph error envelope ({ "error": { "code", "message" } }) - is surfaced on
    # $ErrorRecord.ErrorDetails.Message; fall back to the exception message when no body is present.
    function Get-GraphWriteError {
        param(
            [Parameter(Mandatory = $true)]
            [System.Management.Automation.ErrorRecord]$ErrorRecord
        )

        $body = $null
        if ($ErrorRecord.ErrorDetails -and $ErrorRecord.ErrorDetails.Message) {
            $body = $ErrorRecord.ErrorDetails.Message
        }
        elseif ($ErrorRecord.Exception -and $ErrorRecord.Exception.Message) {
            $body = $ErrorRecord.Exception.Message
        }

        $code = $null
        $message = $null
        if ($body) {
            try {
                $parsed = $body | ConvertFrom-Json -ErrorAction Stop
                if ($parsed -and ($parsed.PSObject.Properties.Name -contains 'error') -and $parsed.error) {
                    $code = $parsed.error.code
                    $message = $parsed.error.message
                }
            }
            catch {
                # The body was not JSON (e.g. a raw exception message); fall back to the raw text.
            }
        }

        if (-not $message) {
            $message = $body
        }

        [pscustomobject]@{
            Code    = $code
            Message = $message
        }
    }

    # Verify whether a delegated oauth2PermissionGrant actually exists, using the already-connected
    # Microsoft Graph session. Invoke-MgGraphRequest is provided by Microsoft.Graph.Authentication
    # (already loaded), so - unlike Get-MgOauth2PermissionGrant - it does NOT trigger an auto-import
    # of Microsoft.Graph.Identity.SignIns, which fails against an already-loaded, differently
    # versioned Microsoft.Graph.Authentication ("Assembly ... already loaded"). Pages through every
    # result set via the @odata.nextLink continuation link.
    #
    # A grant is only considered a match when its consentType ALSO equals the requested value. This
    # function underpins idempotency: the grants we create are tenant-wide admin consents
    # (consentType = 'AllPrincipals'). A delegated grant with consentType = 'Principal' records a
    # single user's own consent for one principalId and does NOT satisfy the AllPrincipals grant, so
    # matching on resourceId + scope alone would wrongly treat a per-user consent as the tenant-wide
    # admin consent and silently skip (or falsely confirm) the required grant.
    function Test-OAuth2PermissionGrantExists {
        param(
            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ClientObjectId,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ResourceObjectId,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$Scope,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ConsentType
        )

        $escapedClientId = $ClientObjectId -replace "'", "''"
        $uri = "https://graph.microsoft.com/v1.0/oauth2PermissionGrants?`$filter=clientId eq '$escapedClientId'"

        while ($uri) {
            $page = Invoke-MgGraphRequest -Method GET -Uri $uri -OutputType Hashtable -ErrorAction Stop

            $values = $null
            if ($page -and $page.ContainsKey('value')) {
                $values = $page['value']
            }

            foreach ($grant in $values) {
                if (-not $grant) { continue }

                $grantResourceId = if ($grant.ContainsKey('resourceId')) { [string]$grant['resourceId'] } else { $null }
                if ($grantResourceId -ne $ResourceObjectId) { continue }

                # Require the same consent type: an AllPrincipals (tenant-wide admin) grant must not
                # be considered satisfied by a Principal (single-user) consent, and vice versa.
                $grantConsentType = if ($grant.ContainsKey('consentType')) { [string]$grant['consentType'] } else { '' }
                if ($grantConsentType -ne $ConsentType) { continue }

                $grantScope = if ($grant.ContainsKey('scope')) { [string]$grant['scope'] } else { '' }
                $scopes = $grantScope -split '\s+' | Where-Object { $_ }
                if ($scopes -contains $Scope) {
                    return $true
                }
            }

            $uri = if ($page -and $page.ContainsKey('@odata.nextLink')) { [string]$page['@odata.nextLink'] } else { $null }
        }

        return $false
    }

    # Read-only reconciliation performed BEFORE reissuing an ambiguous oauth2PermissionGrants POST
    # after a transient directory-propagation failure (build 51863). A brand-new grant can be
    # invisible to the list endpoint for a short window even though the write already applied
    # (read-your-write lag), so a single successful-but-empty/stale list response is NOT proof of
    # absence. Poll Test-OAuth2PermissionGrantExists a bounded number of times with capped
    # exponential backoff: return $true as soon as a grant matching the clientId, resourceId and
    # scope is observed (so no duplicate POST is issued); return $false only after the reads
    # consistently show it absent (so the write genuinely did not apply and a retry is warranted).
    # A genuine read failure is NOT swallowed here - it propagates so the caller can preserve and
    # re-throw the exact original write error.
    function Confirm-OAuth2PermissionGrantApplied {
        param(
            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ClientObjectId,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ResourceObjectId,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$Scope,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ConsentType,

            [int]$MaxReadAttempts = 3,

            [int]$BaseDelaySeconds = 5,

            [int]$MaxDelaySeconds = 20
        )

        for ($read = 1; $read -le $MaxReadAttempts; $read++) {
            # A real Graph read failure must propagate (Test-OAuth2PermissionGrantExists uses
            # -ErrorAction Stop), so the caller can preserve the original write error.
            if (Test-OAuth2PermissionGrantExists -ClientObjectId $ClientObjectId -ResourceObjectId $ResourceObjectId -Scope $Scope -ConsentType $ConsentType) {
                return $true
            }

            if ($read -lt $MaxReadAttempts) {
                $delay = [math]::Min($MaxDelaySeconds, $BaseDelaySeconds * [math]::Pow(2, $read - 1))
                Write-Warning "Grant for scope '$Scope' not yet visible in Microsoft Graph (reconciliation read $read of $MaxReadAttempts). Retrying read after $delay second backoff."
                Start-Sleep -Seconds $delay
            }
        }

        return $false
    }

    # Create a delegated permission grant idempotently and resiliently.
    #  * Transient directory-propagation failures (HTTP 404 Directory_ObjectNotFound / "Unable to
    #    read the company information from the directory") observed immediately after the service
    #    principals are created are retried with bounded exponential backoff.
    #  * An ambiguous write - most notably the 400 Request_BadRequest "Permission entry already
    #    exists" returned when a prior attempt (or an Azure DevOps task retry) already created the
    #    grant - is resolved by verifying the grant's actual presence, so the operation is
    #    idempotent and never creates a duplicate permission entry.
    #  * On ANY failure the grant is treated as successful only when verification positively
    #    confirms it exists; otherwise the exact original write ErrorRecord is re-thrown, so a real
    #    failure is never masked by a silent success.
    function Grant-OAuth2Permission {
        param(
            [Parameter(Mandatory = $true)]
            [ValidateNotNull()]
            [hashtable]$GrantBody,

            [Parameter(Mandatory = $true)]
            [ValidateNotNullOrEmpty()]
            [string]$ScopeDescription,

            [int]$MaxAttempts = 5
        )

        $clientObjectId = [string]$GrantBody.clientId
        $resourceObjectId = [string]$GrantBody.resourceId
        $scope = [string]$GrantBody.scope
        $consentType = [string]$GrantBody.consentType

        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            try {
                Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($GrantBody | ConvertTo-Json) | Out-Null
                Write-Host "Granted $ScopeDescription permission."
                return
            }
            catch {
                $writeError = $_
                $graphError = Get-GraphWriteError -ErrorRecord $writeError

                # Classify the failure. An authoritative structured Graph error code, when present,
                # is definitive and MUST override the loose human-readable message text: only the
                # exact Directory_ObjectNotFound code is a transient directory-propagation failure.
                # The free-text match ("Unable to read the company information from the directory")
                # is a fallback used ONLY when Graph returned no structured code, so an unrelated
                # error whose message merely mentions the directory is never mistaken for one that
                # should be retried.
                if ($graphError.Code) {
                    $isTransientPropagation = ($graphError.Code -eq 'Directory_ObjectNotFound')
                }
                else {
                    $isTransientPropagation = ($graphError.Message -like '*Unable to read the company information from the directory*')
                }

                # The specific duplicate-grant conflict Graph returns when the permission entry
                # already exists (a prior POST, or an Azure DevOps task retry, already created it).
                # Matched on the exact message text because the structured code is the generic
                # Request_BadRequest; this keeps the conflict handling narrow so unrelated bad
                # requests are never funnelled into the reconciliation path.
                $isAlreadyExistsConflict = ($graphError.Message -like '*Permission entry already exists*')

                if ($isTransientPropagation -and $attempt -lt $MaxAttempts) {
                    # Build 51863: the first POST returned 404 Directory_ObjectNotFound while the
                    # principals were still propagating, yet the write had actually applied; the
                    # blind task retry then hit 400 "Permission entry already exists". Before
                    # reissuing an ambiguous POST, reconcile read-only whether the grant already
                    # applied (tolerating successful-but-empty/stale Graph list responses via
                    # bounded, capped backoff). Only reissue the POST when the reads positively
                    # confirm the grant is still absent.
                    Write-Warning "Transient directory propagation failure granting $ScopeDescription permission (attempt $attempt of $MaxAttempts): $($graphError.Message). Reconciling before any retry."

                    $reconciled = $false
                    try {
                        $reconciled = Confirm-OAuth2PermissionGrantApplied -ClientObjectId $clientObjectId -ResourceObjectId $resourceObjectId -Scope $scope -ConsentType $consentType
                    }
                    catch {
                        # A read failure must never mask the original write error. Surface the original.
                        Write-Warning "Unable to reconcile whether the $ScopeDescription permission already applied: $($_.Exception.Message)"
                        throw $writeError
                    }

                    if ($reconciled) {
                        Write-Host "$ScopeDescription permission already applied despite the transient failure; grant is idempotently satisfied (no re-POST)."
                        return
                    }

                    # Reads succeeded but the grant is genuinely absent: the write did not apply,
                    # so it is safe to reissue the POST.
                    continue
                }

                # A non-retryable error, an ambiguous "already exists" conflict, or exhausted
                # transient retries: confirm the grant's real state before deciding success/failure.
                Write-Warning "Received failure when granting $ScopeDescription permission: $($graphError.Message)"

                # An ambiguous write - the "Permission entry already exists" conflict (most often the
                # second POST issued after reconciliation found the first write's result still
                # invisible) or an exhausted transient propagation failure - may have actually applied
                # yet be invisible to the list endpoint for a short window (read-your-write lag). Use
                # the SAME bounded, capped-backoff read-only reconciliation before failing, so a grant
                # that becomes visible after a few empty reads is confirmed without reissuing another
                # POST. A genuine unrelated error is decided by a single verification read; it is NOT
                # retried and does NOT back off.
                $isAmbiguousWrite = $isTransientPropagation -or $isAlreadyExistsConflict

                $verified = $false
                try {
                    if ($isAmbiguousWrite) {
                        $verified = Confirm-OAuth2PermissionGrantApplied -ClientObjectId $clientObjectId -ResourceObjectId $resourceObjectId -Scope $scope -ConsentType $consentType
                    }
                    else {
                        $verified = Test-OAuth2PermissionGrantExists -ClientObjectId $clientObjectId -ResourceObjectId $resourceObjectId -Scope $scope -ConsentType $consentType
                    }
                }
                catch {
                    # Verification must never mask the original write failure. Surface the original.
                    Write-Warning "Unable to verify whether the $ScopeDescription permission already exists: $($_.Exception.Message)"
                    throw $writeError
                }

                if ($verified) {
                    Write-Host "$ScopeDescription permission already exists; grant is idempotently satisfied."
                    return
                }

                throw $writeError
            }
        }
    }

    $bodyForReadPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $windowsAadObjectId
        scope = $userReadScope
    }

    Grant-OAuth2Permission -GrantBody $bodyForReadPermisions -ScopeDescription $userReadScope

    # This permission allows the client app to talk to the fhir-server (resource) without asking for user consent
    $bodyForApiPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $resourceApiObjectId
        scope = $userImpersonationScope
    }

    Grant-OAuth2Permission -GrantBody $bodyForApiPermisions -ScopeDescription $userImpersonationScope
}
