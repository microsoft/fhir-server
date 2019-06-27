function Add-AadTestAuthEnvironment {
    <#
    .SYNOPSIS
    Adds all the required components for the test environment in AAD.
    .DESCRIPTION
    .PARAMETER TestAuthEnvironmentPath
    Path for the testauthenvironment.json file
    .PARAMETER EnvironmentName
    Environment name used for the test environment. This is used throughout for making names unique.
    .PARAMETER TenantAdminCredential
    Credentials for a tenant admin user. Needed to grant admin consent to client apps.
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
        [string]$EnvironmentLocation = "West US",

        [Parameter(Mandatory = $true)]
        [ValidateNotNull()]
        [pscredential]$TenantAdminCredential,
        
        [Parameter(Mandatory = $false )]
        [String]$WebAppSuffix = "azurewebsites.net",

        [Parameter(Mandatory = $false)]
        [string]$ResourceGroupName = $EnvironmentName,

        [parameter(Mandatory = $false)]
        [string]$KeyVaultName = "$EnvironmentName-ts"
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

    $keyVault = Get-AzureRmKeyVault -VaultName $KeyVaultName

    if (!$keyVault) {
        Write-Host "Creating keyvault with the name $KeyVaultName"
        New-AzureRmKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName -Location $EnvironmentLocation | Out-Null
    }

    $retryCount = 0
    # Make sure key vault exists and is ready
    while (!(Get-AzureRmKeyVault -VaultName $KeyVaultName )) {
        $retryCount += 1

        if ($retryCount -gt 7) {
            throw "Could not connect to the vault $KeyVaultName"
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
        Set-AzureRmKeyVaultAccessPolicy -VaultName $KeyVaultName -ObjectId $currentObjectId -PermissionsToSecrets Get,List,Set
    }

    Write-Host "Ensuring API application exists"

    $fhirServiceAudience = Get-ServiceAudience -EnvironmentName $EnvironmentName -WebAppSuffix $WebAppSuffix

    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

    if (!$application) {
        $newApplication = New-FhirServerApiApplicationRegistration -FhirServiceAudience $fhirServiceAudience
        
        # Change to use applicationId returned
        $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience
    }

    Write-Host "Setting roles on API Application"
    $appRoles = ($testAuthEnvironment.roles | Select -ExpandProperty name)
    Set-FhirServerApiApplicationRoles -ApiAppId $application.AppId -AppRoles $appRoles | Out-Null

    Write-Host "Ensuring users and role assignments for API Application exist"
    $environmentUsers = Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantDomain $tenantInfo.TenantDomain -ApiAppId $application.AppId -UserConfiguration $testAuthEnvironment.Users -KeyVaultName $KeyVaultName

    $environmentClientApplications = @()

    Write-Host "Ensuring client application exists"
    foreach ($clientApp in $testAuthEnvironment.clientApplications) {
        $displayName = Get-ApplicationDisplayName -EnvironmentName $EnvironmentName -AppId $clientApp.Id
        $aadClientApplication = Get-AzureAdApplicationByDisplayName $displayName

        $publicClient = -not $clientApp.roles

        if (!$aadClientApplication) {

            $aadClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient:$publicClient

            $secretSecureString = ConvertTo-SecureString $aadClientApplication.AppSecret -AsPlainText -Force

        }
        else {
            $existingPassword = Get-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId | Remove-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
            $newPassword = New-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
            
            $secretSecureString = ConvertTo-SecureString $newPassword.Value -AsPlainText -Force
        }
            
        if ($publicClient) {
            Grant-ClientAppAdminConsent -AppId $aadClientApplication.AppId -TenantAdminCredential $TenantAdminCredential

            # The public client (native app) is being used as SMART on FHIR client app in testing.
            New-FhirServerSmartClientReplyUrl -AppId $aadClientApplication.AppId -FhirServerUrl $fhirServiceAudience -ReplyUrl "https://localhost:6001/sampleapp/index.html"
        }

        $environmentClientApplications += @{
            id          = $clientApp.Id
            displayName = $displayName
            appId       = $aadClientApplication.AppId
        }
        
        $appIdSecureString = ConvertTo-SecureString -String $aadClientApplication.AppId -AsPlainText -Force
        Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--id" -SecretValue $appIdSecureString | Out-Null
        Set-AzureKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--secret" -SecretValue $secretSecureString | Out-Null

        Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -AppId $aadClientApplication.AppId -AppRoles $clientApp.roles | Out-Null
    }

    @{
        keyVaultName                  = $KeyVaultName
        environmentUsers              = $environmentUsers
        environmentClientApplications = $environmentClientApplications
    }
}
