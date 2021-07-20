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

    # There currently is no documented or supported way of programatically
    # granting admin consent. So for now we resort to a hack. 
    # We call an API that is used from the portal. An admin *user* is required for this, a service principal will not work.
    # Also, the call can fail when the app has just been created, so we include a retry loop. 

    $windowsAadServicePrincipal = Get-AzureAdServicePrincipalByAppId -AppId "00000002-0000-0000-c000-000000000000"
    $windowsAadObjectId = $windowsAadServicePrincipal.ObjectId
    $resourceApiServicePrincipal = Get-AzureAdServicePrincipalByAppId -AppId $ResourceApplicationId
    $resourceApiObjectId = $resourceApiServicePrincipal.ObjectId
    $clientServicePrincipal = Get-AzureAdServicePrincipalByAppId -AppId $AppId
    $clientObjectId = $clientServicePrincipal.ObjectId

    $header = @{
        'Authorization'          = 'Bearer ' + $response.access_token
        'Content-Type' = 'application/json'
    }

    $permissionGrantUrl = "https://graph.microsoft.com/v1.0/oauth2PermissionGrants"

    $bodyForReadPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $windowsAadObjectId
        scope = "User.Read"
    }

    while ($true) {
        try {
            Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForReadPermisions | ConvertTo-Json) -ErrorVariable error 
        }
        catch {
            if ($retryCount -lt 3) {
                $retryCount++
                Write-Warning "Received failure when posting to $permissionGrantUrl to grant user.read permission. Will retry in 10 seconds."
                Write-Warning "Error message: $error"
                Start-Sleep -Seconds 10
            }
            else {
                throw
            }    
        }
    }

    $bodyForApiPermisions = @{
        clientId = $clientObjectId
        consentType = "AllPrincipals"
        resourceId = $resourceApiObjectId
        scope = "user_impersonation"
    }

    while ($true) {
        try {
            Invoke-RestMethod -Uri $permissionGrantUrl -Headers $header -Method POST -Body ($bodyForApiPermisions | ConvertTo-Json) -ErrorVariable error 
        }
        catch {
            if ($retryCount -lt 3) {
                $retryCount++
                Write-Warning "Received failure when posting to $permissionGrantUrl to grant user_impersonation permission. Will retry in 10 seconds."
                Write-Warning "Error message: $error"
                Start-Sleep -Seconds 10
            }
            else {
                throw
            }    
        }
    }
}
