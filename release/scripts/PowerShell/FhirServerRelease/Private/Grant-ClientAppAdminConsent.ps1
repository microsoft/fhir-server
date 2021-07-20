function Grant-ClientAppAdminConsent {
    <#
    .SYNOPSIS
    Grants admin consent to a client app, so that users of the app are 
    not required to consent to the app calling the FHIR apli app on their behalf.
    .PARAMETER AppId
    The client application app ID.
    .PARAMETER TenantAdminCredential
    Credentials for a tenant admin user
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
        [string]$AccessToken,

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [string]$ResourceApplicationId
    )

    Set-StrictMode -Version Latest

    Write-Host "Granting admin consent for app ID $AppId"

    # Get token to take to graph api
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
        Write-Warning "Failed to get authorization to talk to graph api."
        Write-Warning "Error message: $error"

        throw
    }

    $windowsAadId = "00000002-0000-0000-c000-000000000000"
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

    try {
        $response = Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForReadPermisions | ConvertTo-Json) -ErrorVariable error
    }
    catch {
        Write-Warning "Received failure when posting to $permissionGrantUrl to grant user.read permission."
        Write-Warning "Error message: $error"

        throw
    }

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
