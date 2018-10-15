function Remove-AadTestAuthEnvironment {
    <#
    .SYNOPSIS
    Removes the AAD components for the test environment in AAD.
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

    # Get current AzureAd context
    try {
        Get-AzureRmContext | Out-Null
    } 
    catch {
        throw "Please log in to Azure RM with Login-AzureRmAccount cmdlet before proceeding"
    }

    Write-Host "Tearing down test authorization environment for AAD"

    $TestAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $fhirServiceAudience = Get-ServiceAudience $EnvironmentName

    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

    if ($application) {
        Write-Host "Removing API application ${fhirServiceAudience}"
        Remove-AzureAdApplication -ObjectId $application.ObjectId | Out-Null
    }

    foreach ($user in $TestAuthEnvironment.Users) {
        $upn = Get-UserUpn -EnvironmentName $EnvironmentName -UserId $user.Id -TenantDomain $tenantInfo.TenantDomain
        $aadUser = Get-AzureAdUser -Filter "userPrincipalName eq '${upn}'"

        if ($aadUser) {
            Write-Host "Removing user ${upn}"
            Remove-AzureAdUser -ObjectId $aadUser.Objectid | Out-Null
        }
    }

    foreach ($clientApp in $TestAuthEnvironment.ClientApplications) {
        $displayName = "${EnvironmentName}-$($clientApp.Id)"
        $aadClientApplication = Get-AzureAdApplicationByDisplayName $displayName
        
        if ($aadClientApplication) {
            Write-Host "Removing application ${displayName}"
            Remove-AzureAdApplication -ObjectId $aadClientApplication.ObjectId | Out-Null
        }
    }
}