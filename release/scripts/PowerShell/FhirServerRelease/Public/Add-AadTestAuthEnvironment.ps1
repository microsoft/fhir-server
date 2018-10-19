function Add-AadTestAuthEnvironment {
    <#
    .SYNOPSIS
    Adds all the required components for the test environment in AAD.
    .DESCRIPTION
    .PARAMETER TestAuthEnvironmentPath
    Path for the testauthenvironment.json file
    .PARAMETER EnvironmentName
    Environment name used for the test environment. This is used throughout for making names unique.
    #>
    param
    (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TestAuthEnvironmentPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentName,

        [Parameter(Mandatory = $false)]
        [string]$EnvironmentLocation = "West US"
    )
    
    Set-StrictMode -Version Latest

    # Get current AzureAd context
    try {
        $tenantInfo = Get-AzureADCurrentSessionInfo -ErrorAction Stop
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    # Get current AzureRm context
    try {
        $azureRmContext = Get-AzureRmContext
    } 
    catch {
        throw "Please log in to Azure RM with Login-AzureRmAccount cmdlet before proceeding"
    }

    Write-Host "Setting up Test Authorization Environment for AAD"

    $testAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $keyVaultName = "$EnvironmentName-ts"

    $keyVault = Get-AzureRmKeyVault -VaultName $keyVaultName

    if (!$keyVault) {
        Write-Host "Creating keyvault with the name $keyVaultName"
        New-AzureRmKeyVault -VaultName $keyVaultName -ResourceGroupName $EnvironmentName -Location $EnvironmentLocation | Out-Null
    }

    $retryCount = 0
    # Make sure key vault exists and is ready
    while (!(Get-AzureRmKeyVault -VaultName $keyVaultName )) {
        $retryCout += 1

        if ($retry -gt 7) {
            throw "Could not connect to the vault $keyVaultName"
        }

        sleep 10
    }
    
    if ($azureRmContext.Account.Type -eq "User") {
        Write-Host "Current context is user: $($azureRmContext.Account.Id)"
        $currentObjectId = (Get-AzureRmADUser -UserPrincipalName $azureRmContext.Account.Id).Id
    }
    elseif ($azureRmContext.Account.Type -eq "ServicePrincipal") {
        Write-Host "Current context is service principal: $($azureRmContext.Account.Id)"
        $currentObjectId = (Get-AzureRmADServicePrincipal -ServicePrincipalName $azureRmContext.Account.Id).Id
    }
    else {
        Write-Host "Current context is account of type '$($azureRmContext.Account.Type)' with id of '$($azureRmContext.Account.Id)"
        throw "Running as an unsupported account type. Please use either a 'User' or 'Service Principal' to run this command"
    }

    if ($currentObjectId) {
        Write-Host "Adding permission to keyvault for $currentObjectId"
        Set-AzureRmKeyVaultAccessPolicy -VaultName $keyVaultName -ObjectId $currentObjectId -PermissionsToSecrets Get, Set
    }

    Write-Host "Ensuring API application exists"

    $fhirServiceAudience = Get-ServiceAudience $EnvironmentName

    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

    if (!$application) {
        $newApplication = New-FhirServerApiApplicationRegistration -FhirServiceAudience $fhirServiceAudience
        
        # Change to use applicationId returned
        $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience
    }

    Write-Host "Setting roles on API Application"
    Set-FhirServerApiApplicationRoles -ObjectId $application.ObjectId -RoleConfiguration $testAuthEnvironment.Roles | Out-Null

    $servicePrincipal = Get-AzureAdServicePrincipalByAppId $application.AppId

    Write-Host "Ensuring users and role assignments for API Application exist"
    $environmentUsers = Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantDomain $tenantInfo.TenantDomain -ServicePrincipalObjectId $servicePrincipal.ObjectId -UserConfiguration $testAuthEnvironment.Users -KeyVaultName $keyVaultName

    $environmentClientApplications = @()

    Write-Host "Ensuring client application exists"
    foreach ($clientApp in $testAuthEnvironment.ClientApplications) {
        $displayName = Get-ApplicationDisplayName -EnvironmentName $EnvironmentName -AppId $clientApp.Id
        $aadClientApplication = Get-AzureAdApplicationByDisplayName $displayName

        if (!$aadClientApplication) {
            $publicClient = $false

            if (!$clientApp.Roles) {
                $publicClient = $true
            }

            $aadClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient:$publicClient

            $secretSecureString = ConvertTo-SecureString $aadClientApplication.AppSecret -AsPlainText -Force
            
            if ($publicClient) {
                Grant-OAuth2PermissionsToApp $aadClientApplication $azureRmContext
            }
        }
        else {
            $existingPassword = Get-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId | Remove-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
            $newPassword = New-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
            
            $secretSecureString = ConvertTo-SecureString $newPassword.Value -AsPlainText -Force
        }

        $environmentClientApplications += @{
            id          = $clientApp.Id
            displayName = $displayName
            appId       = $aadClientApplication.AppId
        }
        
        Set-AzureKeyVaultSecret -VaultName $keyVaultName -Name "$displayName-secret" -SecretValue $secretSecureString | Out-Null

        $aadClientServicePrincipal = Get-AzureAdServicePrincipalByAppId $aadClientApplication.AppId

        Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -ObjectId $aadClientServicePrincipal.ObjectId -Roles $clientApp.Roles | Out-Null
    }

    @{
        keyVaultName                  = $keyVaultName
        environmentUsers              = $environmentUsers
        environmentClientApplications = $environmentClientApplications
    }
}

Function Grant-OAuth2PermissionsToApp{
    Param(
        [Parameter(Mandatory=$true)]$azureClientApp,
        [Parameter(Mandatory=$true)]$context
    )
    $refreshToken = $context.TokenCache.ReadItems().RefreshToken
    $body = "grant_type=refresh_token&refresh_token=$($refreshToken)&resource=74658136-14ec-4630-ad9b-26e160ff0fc6"
    $apiToken = Invoke-RestMethod "$azureClientApp.TokenUrl" -Method POST -Body $body -ContentType 'application/x-www-form-urlencoded'
    $header = @{
    'Authorization' = 'Bearer ' + $apiToken.access_token
    'X-Requested-With'= 'XMLHttpRequest'
    'x-ms-client-request-id'= [guid]::NewGuid()
    'x-ms-correlation-id' = [guid]::NewGuid()}
    $url = "$azureClientApp.AuthUrl&client_id=$azureClientApp.AppId&response_type=code&redirect_uri=$azureClientApp.ReplyUrl&prompt=admin_consent"
    Invoke-RestMethod –Uri $url –Headers $header –Method POST -ErrorAction Stop
}