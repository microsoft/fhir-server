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
        [String]$TenantId,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [String]$ClientId,

        [Parameter(Mandatory = $true )]
        [ValidateNotNullOrEmpty()]
        [SecureString]$ClientSecret
    )

    Set-StrictMode -Version Latest
    
    # Get current Microsoft Graph context
    try {
        $context = Get-MgContext -ErrorAction Stop
        if (-not $context) {
            throw "No Microsoft Graph session found"
        }
        # Get organization info to extract tenant domain
        $organization = Get-MgOrganization | Select-Object -First 1
        $tenantInfo = @{
            TenantDomain = $organization.VerifiedDomains | Where-Object { $_.IsDefault -eq $true } | Select-Object -ExpandProperty Name
        }
    } 
    catch {
        throw "Please log in to Microsoft Graph with Connect-MgGraph cmdlet before proceeding"
    }

    Write-Host "Tearing down test authorization environment for Microsoft Graph"

    $testAuthEnvironment = Get-Content -Raw -Path $TestAuthEnvironmentPath | ConvertFrom-Json

    $fhirServiceAudience = Get-ServiceAudience -ServiceName $EnvironmentName -TenantId $TenantId

    $ClientSecretCredential = New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $ClientId, $ClientSecret

    
    Install-Module -Name Microsoft.Graph -Force

    Connect-MgGraph -TenantId $tenantId -ClientSecretCredential $ClientSecretCredential

    $application = Get-AzureAdApplicationByIdentifierUri $fhirServiceAudience

    if ($application) {
        Write-Host "Removing API application $fhirServiceAudience"
        Remove-MgApplication -ApplicationId $application.Id | Out-Null
    }

    foreach ($user in $testAuthEnvironment.Users) {
        $upn = Get-UserUpn -EnvironmentName $EnvironmentName -UserId $user.Id -TenantDomain $tenantInfo.TenantDomain
        try {
            $mgUser = Get-MgUser -Filter "userPrincipalName eq '$upn'" -ErrorAction Stop
            if ($mgUser) {
                Write-Host "Removing user $upn"
                Remove-MgUser -UserId $mgUser.Id | Out-Null
            }
        }
        catch {
            if ($_.Exception.Message -like "*does not exist*" -or $_.Exception.Message -like "*NotFound*") {
                Write-Host "User $upn not found - skipping"
            }
            else {
                Write-Warning "Error accessing user $upn : $($_.Exception.Message)"
            }
        }
    }

    foreach ($clientApp in $testAuthEnvironment.ClientApplications) {
        $displayName = Get-ApplicationDisplayName -EnvironmentName $EnvironmentName -AppId $clientApp.Id
        $mgClientApplication = Get-AzureAdApplicationByDisplayName $displayName
        
        if ($mgClientApplication) {
            Write-Host "Removing application $displayName"
            Remove-MgApplication -ApplicationId $mgClientApplication.Id | Out-Null
        }
    }
}
