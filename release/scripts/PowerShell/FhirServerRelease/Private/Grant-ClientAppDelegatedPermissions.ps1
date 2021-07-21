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

    # Get token to talk to graph api
    $tenantId = (Get-AzureADCurrentSessionInfo).TenantId.ToString()

    $adTokenUrl = "https://login.microsoftonline.com/$tenantId/oauth2/token"
    $resource = "https://graph.microsoft.com/"

    $body = @{
        grant_type = "password"
        username   = $TenantAdminCredential.GetNetworkCredential().UserName
        password   = $TenantAdminCredential.GetNetworkCredential().Password
        resource   = $resource
        client_id  = "1950a258-227b-4e31-a9cf-717495945fc2" # Microsoft Azure PowerShell
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
    $windowsAadServicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$windowsAadId'"
    $windowsAadObjectId = $windowsAadServicePrincipal.ObjectId

    $resourceApiServicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$ResourceApplicationId'"
    $resourceApiObjectId = $resourceApiServicePrincipal.ObjectId

    $clientServicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$AppId'"
    $clientObjectId = $clientServicePrincipal.ObjectId

    $header = @{
        'Authorization' = 'Bearer ' + $response.access_token
        'Content-Type' = 'application/json'
    }

    $permissionGrantUrl = "https://graph.microsoft.com/v1.0/oauth2PermissionGrants"

    $bodyForReadPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $windowsAadObjectId
        scope = "User.Read"
    }

    # TODO: Handle scenario where application might already have these permissions.

    try {
        $response = Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForReadPermisions | ConvertTo-Json) -ErrorVariable error
    }
    catch {
        Write-Warning "Received failure when posting to $permissionGrantUrl to grant user.read permission."
        Write-Warning "Error message: $error"

        throw
    }

    # This permission allows the client app to talk to the fhir-server (resource) without asking for user consent
    $bodyForApiPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $resourceApiObjectId
        scope = "user_impersonation"
    }

    try {
        $response = Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForApiPermisions | ConvertTo-Json) -ErrorVariable error
    }
    catch {
        Write-Warning "Received failure when posting to $permissionGrantUrl to grant user_impersonation permission."
        Write-Warning "Error message: $error"

        throw
    }
}
