<#
.SYNOPSIS
Setup the test environment for AAD.
.DESCRIPTION
.PARAMETER TestAuthorizationEnvironmentPath
Path for the testauthorizationenvironment.json file
.PARAMETER EnvironmentName
Environment name used for the test environment. This is used throughout for making names unique.
#>
param
(
        [Parameter(Mandatory = $true)]
        [string]$TestAuthorizationEnvironmentPath,

        [Parameter(Mandatory = $true)]
        [string]$EnvironmentName
)

Import-Module (Resolve-Path('../../samples/scripts/PowerShell/FhirServer/FhirServer.psm1')) -Force
Import-Module (Resolve-Path('FhirServerRelease.psm1')) -Force

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

$testAuthorizationEnvironment = Get-Content -Raw -Path $TestAuthorizationEnvironmentPath | ConvertFrom-Json

$keyVaultName = "${EnvironmentName}-ts"

$keyVault = Get-AzureRmKeyVault -VaultName $keyVaultName

if(!$keyVault)
{
    New-AzureRmKeyVault -VaultName $keyVaultName -ResourceGroupName ${EnvironmentName} -Location 'East US' | Out-Null
}

# Make sure key vault exists and is ready
while (!(Get-AzureRmKeyVault -VaultName $keyVaultName ))
{
   sleep 10
}

Write-Host "Ensuring API application exists"

$fhirServiceAudience = "https://${EnvironmentName}.azurewebsites.net"

$application = Get-AzureAdApplication -Filter "identifierUris/any(uri:uri eq '${fhirServiceAudience}')"

if(!$application)
{
    $newApplication = New-FhirServerApiApplicationRegistration -FhirServiceAudience $fhirServiceAudience
    
    # Change to use applicationId returned
    $application = Get-AzureAdApplication -Filter "DisplayName eq '${fhirServiceAudience}'"
}

Write-Host "Setting roles on API Application"
Set-FhirServerApiApplicationRoles -ObjectId $application.ObjectId -RoleConfiguration $testAuthorizationEnvironment.Roles | Out-Null

$servicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$($application.AppId)'"

Write-Host "Ensuring users and role assignments for API Application exist"
Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantDomain $tenantInfo.TenantDomain -ServicePrincipalObjectId $servicePrincipal.ObjectId -UserConfiguration $testAuthorizationEnvironment.Users -KeyVaultName $keyVaultName | Out-Null

Write-Host "Ensuring client application exists"
foreach($clientApp in $testAuthorizationEnvironment.ClientApplications)
{
    $displayName = "${EnvironmentName}-$($clientApp.Id)"
    $aadClientApplication = Get-AzureAdApplication -Filter "DisplayName eq '$displayName'"

    if(!$aadClientApplication)
    {
        $publicClient = $false

        if(!$clientApp.Roles)
        {
            $publicClient = $true
        }

        $aadClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient $publicClient

        $secretSecureString = ConvertTo-SecureString $aadClientApplication.AppSecret -AsPlainText -Force
    }
    else
    {
        $existingPassword = Get-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId | Remove-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
        $newPassword = New-AzureADApplicationPasswordCredential -ObjectId $aadClientApplication.ObjectId
        
        $secretSecureString = ConvertTo-SecureString $newPassword.Value -AsPlainText -Force
    }
    
    Set-AzureKeyVaultSecret -VaultName $keyVaultName -Name "${displayName}-secret" -SecretValue $secretSecureString | Out-Null

    $aadClientServicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$($aadClientApplication.AppId)'"

    Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -ObjectId $aadClientServicePrincipal.ObjectId -Roles $clientApp.Roles | Out-Null
}

