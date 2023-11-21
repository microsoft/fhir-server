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

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [String]$TenantId,

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

    # Get current Az context
    try {
        $azContext = Get-AzContext
    }
    catch {
        throw "Please log in to Azure RM with Login-AzAccount cmdlet before proceeding"
    }

    Write-Host "Setting up Test Authorization Environment for AAD"

    $testAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $keyVault = Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName

    if (!$keyVault) {
        Write-Host "Creating keyvault with the name $KeyVaultName"
        New-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName -Location $EnvironmentLocation -EnableRbacAuthorization | Out-Null
    }

    $retryCount = 0
    # Make sure key vault exists and is ready
    while (!(Get-AzKeyVault -VaultName $KeyVaultName -ResourceGroupName $ResourceGroupName )) {
        $retryCount += 1

        if ($retryCount -gt 20) {
            throw "Could not connect to the vault $KeyVaultName"
        }

        Write-Warning "Waiting on keyvault. Retry $retryCount"
        sleep 30
    }

    Write-Host "Setting permissions on keyvault for current context"
    if ($azContext.Account.Type -eq "User") {
        Write-Host "Current context is user: $($azContext.Account.Id)"
        $currentObjectId = (Get-AzADUser -UserPrincipalName $azContext.Account.Id).Id
    }
    elseif ($azContext.Account.Type -eq "ServicePrincipal") {
        Write-Host "Current context is service principal: $($azContext.Account.Id)"
        $currentObjectId = (Get-AzADServicePrincipal -ServicePrincipalName $azContext.Account.Id).Id
    }
    else {
        Write-Host "Current context is account of type '$($azContext.Account.Type)' with id of '$($azContext.Account.Id)"
        throw "Running as an unsupported account type. Please use either a 'User' or 'Service Principal' to run this command"
    }

    if ($currentObjectId) {
        Write-Host "Adding permission to keyvault for $currentObjectId"
        New-AzKeyVaultRoleAssignment -ObjectId $currentObjectId -RoleDefinitionName "Key Vault Secrets Officer" | Out-Null
    }

    Write-Host "Ensuring API application exists"

    $fhirServiceAudience = Get-ServiceAudience -ServiceName $EnvironmentName -TenantId $TenantId

    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

    if (!$application) {
        $newApplication = New-FhirServerApiApplicationRegistration -FhirServiceAudience $fhirServiceAudience

        # Change to use applicationId returned
        $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience
    }

    Write-Host "Setting roles on API Application"
    $appRoles = ($testAuthEnvironment.users.roles + $testAuthEnvironment.clientApplications.roles) | Select-Object -Unique
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
            Grant-ClientAppDelegatedPermissions -AppId $aadClientApplication.AppId -TenantAdminCredential $TenantAdminCredential -ResourceApplicationId $application.AppId

            # The public client (native app) is being used as SMART on FHIR client app in testing.
            New-FhirServerSmartClientReplyUrl -AppId $aadClientApplication.AppId -FhirServerUrl $fhirServiceAudience -ReplyUrl "https://localhost:6001/sampleapp/index.html"
        }

        $environmentClientApplications += @{
            id          = $clientApp.Id
            displayName = $displayName
            appId       = $aadClientApplication.AppId
        }

        $appIdSecureString = ConvertTo-SecureString -String $aadClientApplication.AppId -AsPlainText -Force
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--id" -SecretValue $appIdSecureString | Out-Null
        Set-AzKeyVaultSecret -VaultName $KeyVaultName -Name "app--$($clientApp.Id)--secret" -SecretValue $secretSecureString | Out-Null

        Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -AppId $aadClientApplication.AppId -AppRoles $clientApp.roles | Out-Null
    }

    @{
        keyVaultName                  = $KeyVaultName
        environmentUsers              = $environmentUsers
        environmentClientApplications = $environmentClientApplications
    }
}
