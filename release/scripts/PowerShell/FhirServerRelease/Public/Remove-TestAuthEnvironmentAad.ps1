function Remove-TestAuthEnvironmentAad {
    <#
    .SYNOPSIS
    Removes the AAD components for the test environment in AAD.
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

    # Get current AzureAd context
    try {
        $tenantInfo = Get-AzureADCurrentSessionInfo -ErrorAction Stop
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    # Get current AzureAd context
    try {
        $existingContext = Get-AzureRmContext
    } 
    catch {
        throw "Please log in to Azure RM with Login-AzureRmAccount cmdlet before proceeding"
    }

    Write-Host "Tearing down test authorization environment for AAD"

    $testAuthorizationEnvironment = Get-Content -Raw -Path $TestAuthorizationEnvironmentPath | ConvertFrom-Json

    $fhirServiceAudience = "https://${EnvironmentName}.azurewebsites.net"

    $application = Get-AzureAdApplication -Filter "identifierUris/any(uri:uri eq '${fhirServiceAudience}')"

    if($application)
    {
        Write-Host "Removing API application ${fhirServiceAudience}"
        Remove-AzureAdApplication -ObjectId $application.ObjectId | Out-Null
    }

    foreach($user in $testAuthorizationEnvironment.Users)
    {
        $upn = "${EnvironmentName}-$($user.id)@$($tenantInfo.TenantDomain)"
        $aadUser = Get-AzureAdUser -Filter "userPrincipalName eq '${upn}'"

        if($aadUser)
        {
            Write-Host "Removing user ${upn}"
            Remove-AzureAdUser -ObjectId $aadUser.Objectid | Out-Null
        }
    }

    foreach($clientApp in $testAuthorizationEnvironment.ClientApplications)
    {
        $displayName = "${EnvironmentName}-$($clientApp.Id)"
        $aadClientApplication = Get-AzureAdApplication -Filter "DisplayName eq '${displayName}'"
        
        if($aadClientApplication)
        {
            Write-Host "Removing application ${displayName}"
            Remove-AzureAdApplication -ObjectId $aadClientApplication.ObjectId | Out-Null
        }
    }
}