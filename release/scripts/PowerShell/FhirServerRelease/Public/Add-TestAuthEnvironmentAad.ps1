function Add-TestAuthEnvironmentAad {
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
        [string]$TestAuthEnvironmentPath,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
    )

    # Get current AzureAd context
    try {
        $tenantInfo = Get-AzureADCurrentSessionInfo -ErrorAction Stop
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    # Get current AzureRm context
    try {
        $existingContext = Get-AzureRmContext
    } 
    catch {
        throw "Please log in to Azure RM with Login-AzureRmAccount cmdlet before proceeding"
    }

    Write-Host "Setting up Test Authorization Environment for AAD"

    $TestAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $keyVaultName = "${EnvironmentName}-ts"

    $keyVault = Get-AzureRmKeyVault -VaultName $keyVaultName

    if (!$keyVault) {
        New-AzureRmKeyVault -VaultName $keyVaultName -ResourceGroupName ${EnvironmentName} -Location 'East US' | Out-Null
    }

    $retryCount = 0
    # Make sure key vault exists and is ready
    while (!(Get-AzureRmKeyVault -VaultName $keyVaultName )) {
        $retryCout += 1

        if ($retry -gt 7) {
            throw "Could not connect to the vault ${keyVaultName}"
        }

        sleep 10
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
    Set-FhirServerApiApplicationRoles -ObjectId $application.ObjectId -RoleConfiguration $TestAuthEnvironment.Roles | Out-Null

    $servicePrincipal = Get-AzureAdServicePrincipalByAppId $application.AppId

    Write-Host "Ensuring users and role assignments for API Application exist"
    Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantDomain $tenantInfo.TenantDomain -ServicePrincipalObjectId $servicePrincipal.ObjectId -UserConfiguration $TestAuthEnvironment.Users -KeyVaultName $keyVaultName | Out-Null

    Write-Host "Ensuring client application exists"
    foreach ($clientApp in $TestAuthEnvironment.ClientApplications) {
        $displayName = "${EnvironmentName}-$($clientApp.Id)"
        $aadClientApplication = Get-AzureAdApplicationByDisplayName $displayName

        if (!$aadClientApplication) {
            $publicClient = $false

            if (!$clientApp.Roles) {
                $publicClient = $true
            }

            $aadClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient:$publicClient

            $secretSecureString = ConvertTo-SecureString $aadClientApplication.AppSecret -AsPlainText -Force
        }
        else {
            $existingPassword = Get-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId | Remove-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
            $newPassword = New-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
            
            $secretSecureString = ConvertTo-SecureString $newPassword.Value -AsPlainText -Force
        }
        
        Set-AzureKeyVaultSecret -VaultName $keyVaultName -Name "${displayName}-secret" -SecretValue $secretSecureString | Out-Null

        $aadClientServicePrincipal = Get-AzureAdServicePrincipalByAppId $aadClientApplication.AppId

        Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -ObjectId $aadClientServicePrincipal.ObjectId -Roles $clientApp.Roles | Out-Null
    }
}