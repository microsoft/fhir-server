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

    # A failed POST may have applied but remain invisible on an immediate Graph read.
    # Poll read-only before reissuing; let read errors propagate to preserve the write error.
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

            [int]$MaxReadAttempts = 3
        )

        for ($read = 1; $read -le $MaxReadAttempts; $read++) {
            if (Test-OAuth2PermissionGrantExists -ClientObjectId $ClientObjectId -ResourceObjectId $ResourceObjectId -Scope $Scope -ConsentType $ConsentType) {
                return $true
            }

            if ($read -lt $MaxReadAttempts) {
                $delay = 5 * [math]::Pow(2, $read - 1)
                Write-Warning "Grant for scope '$Scope' not yet visible in Microsoft Graph (reconciliation read $read of $MaxReadAttempts). Retrying read after $delay second backoff."
                Start-Sleep -Seconds $delay
            }
        }

        return $false
    }

    # Reconcile ambiguous writes before retrying; surface the original write error unless
    # the requested grant is confirmed.
    function Grant-OAuth2Permission {
        param(
            [Parameter(Mandatory = $true)]
            [ValidateNotNull()]
            [hashtable]$GrantBody,

            [int]$MaxAttempts = 5
        )

        $clientObjectId = [string]$GrantBody.clientId
        $resourceObjectId = [string]$GrantBody.resourceId
        $scope = [string]$GrantBody.scope
        $consentType = [string]$GrantBody.consentType

        for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
            try {
                Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($GrantBody | ConvertTo-Json) | Out-Null
                Write-Host "Granted $scope permission."
                return
            }
            catch {
                $writeError = $_
                $graphError = Get-GraphWriteError -ErrorRecord $writeError

                # A structured Graph code takes precedence over the message fallback.
                if ($graphError.Code) {
                    $isTransientPropagation = ($graphError.Code -eq 'Directory_ObjectNotFound')
                }
                else {
                    $isTransientPropagation = ($graphError.Message -like '*Unable to read the company information from the directory*')
                }

                # Graph reports this conflict with the generic Request_BadRequest code.
                $isAlreadyExistsConflict = ($graphError.Message -like '*Permission entry already exists*')

                Write-Warning "Received failure when granting $scope permission: $($graphError.Message)"

                # A transient 404 or duplicate conflict may have applied despite the error;
                # unrelated failures need only one verification read.
                $isAmbiguousWrite = $isTransientPropagation -or $isAlreadyExistsConflict
                try {
                    if ($isAmbiguousWrite) {
                        $verified = Confirm-OAuth2PermissionGrantApplied -ClientObjectId $clientObjectId -ResourceObjectId $resourceObjectId -Scope $scope -ConsentType $consentType
                    }
                    else {
                        $verified = Test-OAuth2PermissionGrantExists -ClientObjectId $clientObjectId -ResourceObjectId $resourceObjectId -Scope $scope -ConsentType $consentType
                    }
                }
                catch {
                    Write-Warning "Unable to verify whether the $scope permission already exists: $($_.Exception.Message)"
                    throw $writeError
                }

                if ($verified) {
                    Write-Host "$scope permission already exists; grant is idempotently satisfied."
                    return
                }

                if ($isTransientPropagation -and $attempt -lt $MaxAttempts) {
                    continue
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

    Grant-OAuth2Permission -GrantBody $bodyForReadPermisions

    # This permission allows the client app to talk to the fhir-server (resource) without asking for user consent
    $bodyForApiPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $resourceApiObjectId
        scope = $userImpersonationScope
    }

    Grant-OAuth2Permission -GrantBody $bodyForApiPermisions
}
