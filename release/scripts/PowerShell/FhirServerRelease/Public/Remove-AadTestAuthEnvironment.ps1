function Remove-AadTestAuthEnvironment {
    <#
    .SYNOPSIS
    Removes the AAD components for the test environment in AAD.
    .DESCRIPTION
    .PARAMETER TestAuthEnvironmentPath
    Path for the testauthenvironment.json file
    .PARAMETER EnvironmentName
    Environment name used for the test environment. This is used throughout for making names unique.
    .PARAMETER TenantId
    TenantId used for creating service audience while creating AAD application
    #>
    param
    (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$TestAuthEnvironmentPath,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$EnvironmentName,
        
        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [String]$TenantId
    )

    Set-StrictMode -Version Latest
    
    # Get current AzureAd context
    try {
        $tenantInfo = Get-MgContext -ErrorAction Stop
    } 
    catch {
        throw "Please log in to Microsoft Graph with Connect-MgGraph before proceeding"
    }

    Write-Host "Tearing down test authorization environment for AAD"

    $testAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $fhirServiceAudience = Get-ServiceAudience -ServiceName $EnvironmentName -TenantId $TenantId

    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

    if ($application) {
        Write-Host "Removing API application $fhirServiceAudience"
        Remove-MgApplication -ObjectId $application.ObjectId | Out-Null
    }

    foreach ($user in $testAuthEnvironment.Users) {
        $upn = Get-UserUpn -EnvironmentName $EnvironmentName -UserId $user.Id -TenantDomain $tenantInfo.TenantDomain
        $aadUser = Get-MgUser -Filter "userPrincipalName eq '$upn'"

        if ($aadUser) {
            Write-Host "Removing user $upn"
            Remove-MgUser -ObjectId $aadUser.Objectid | Out-Null
        }
    }

    foreach ($clientApp in $testAuthEnvironment.ClientApplications) {
        $displayName = Get-ApplicationDisplayName -EnvironmentName $EnvironmentName -AppId $clientApp.Id
        $aadClientApplication = Get-AzureAdApplicationByDisplayName $displayName
        
        if ($aadClientApplication) {
            Write-Host "Removing application $displayName"
            Remove-MgApplication -ObjectId $aadClientApplication.ObjectId | Out-Null
        }
    }
}
