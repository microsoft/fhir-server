<#
    .SYNOPSIS
    .DESCRIPTION
    .PARAMETER TestAuthorizationEnvironmentPath
    ObjectId for the application
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

Write-Host "Setting up Test Authorization Environment for AAD"

$testAuthorizationEnvironment = Get-Content -Raw -Path $TestAuthorizationEnvironmentPath | ConvertFrom-Json

Write-Host "Ensuring API application exists"

$fhirServiceAudience = "https://${EnvironmentName}.azurewebsites.net"

$application = Get-AzureAdApplication -Filter "DisplayName eq '${fhirServiceAudience}'"

if(!$application)
{
    $newApplication = New-FhirServerApiApplicationRegistration -FhirServiceAudience $fhirServiceAudience
    
    $application = Get-AzureAdApplication -Filter "DisplayName eq '${fhirServiceAudience}'"
}

Write-Host "Setting roles on API Application"
Set-FhirServerApiApplicationRoles -ObjectId $application.ObjectId -RoleConfiguration $testAuthorizationEnvironment.Roles | Out-Null

$servicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$($application.AppId)'"

Write-Host "Ensuring users and role assignments for API Application exist"
Set-FhirServerApiUsers -UserNamePrefix $EnvironmentName -TenantId $tenantInfo.TenantDomain -ServicePrincipalObjectId $servicePrincipal.ObjectId -UserConfiguration $testAuthorizationEnvironment.Users | Out-Null

Write-Host "Ensuring client application exists"
foreach($clientApp in $testAuthorizationEnvironment.ClientApplications)
{
    $displayName = "${EnvironmentName}-$($clientApp.Id)"
    $aadClientApplication = Get-AzureAdApplication -Filter "DisplayName eq '$displayName'"

    if(!$aadClientApplication)
    {
        $aadClientApplication = New-FhirServerClientApplicationRegistration -ApiAppId $application.AppId -DisplayName "$displayName" -PublicClient $true
    }

    $aadClientServicePrincipal = Get-AzureAdServicePrincipal -Filter "appId eq '$($aadClientApplication.AppId)'"

    Set-FhirServerClientAppRoleAssignments -ApiAppId $application.AppId -ObjectId $aadClientServicePrincipal.ObjectId -Roles $clientApp.Roles | Out-Null
}

