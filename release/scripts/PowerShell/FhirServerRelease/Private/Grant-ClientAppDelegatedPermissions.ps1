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

    $bodyForReadPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $windowsAadObjectId
        scope = $userReadScope
    }

    try {
        $response = Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForReadPermisions | ConvertTo-Json) -ErrorVariable error
    }
    catch {
        Write-Warning "Received failure when posting to $permissionGrantUrl to grant $userReadScope permission."
        Write-Warning "Error message: $error"

        $existingPermission = Get-MgOauth2PermissionGrant -All | Where-Object {$_.ClientId -eq $clientObjectId -and $_.ResourceId -eq $windowsAadObjectId -and $_.Scope -eq $userReadScope }
        if($existingPermission) {
            Write-Host "$userReadScope permission already exists."
        }
        else {
            throw
        }
    }

    # This permission allows the client app to talk to the fhir-server (resource) without asking for user consent
    $bodyForApiPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $resourceApiObjectId
        scope = $userImpersonationScope
    }

    try {
        $response = Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForApiPermisions | ConvertTo-Json) -ErrorVariable error
    }
    catch {
        Write-Warning "Received failure when posting to $permissionGrantUrl to grant $userImpersonationScope permission."
        Write-Warning "Error message: $error"

        $existingPermission = Get-MgOauth2PermissionGrant -All | Where-Object {$_.ClientId -eq $clientObjectId -and $_.ResourceId -eq $resourceApiObjectId -and $_.Scope -eq $userImpersonationScope }
        if($existingPermission) {
            Write-Host "$userImpersonationScope permission already exists."
        }
        else {
            throw
        }
    }
}
