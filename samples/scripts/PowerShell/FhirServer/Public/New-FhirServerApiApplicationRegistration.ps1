function New-FhirServerApiApplicationRegistration {
    <#
    .SYNOPSIS
    Create an AAD Application registration for a FHIR server instance.
    .DESCRIPTION
    Create a new AAD Application registration for a FHIR server instance. 
    A FhirServiceName or FhirServiceAudience must be supplied.
    .EXAMPLE
    New-FhirServerApiApplicationRegistration -FhirServiceName "myfhiservice" -AppRoles globalReader,globalExporter
    .EXAMPLE
    New-FhirServerApiApplicationRegistration -FhirServiceAudience "https://myfhirservice.azurewebsites.net" -AppRoles globalReader,globalExporter
    .PARAMETER FhirServiceName
    Name of the FHIR service instance. 
    .PARAMETER FhirServiceAudience
    Full URL of the FHIR service.
    .PARAMETER TenantId
    Will be appended to FHIR service name to form the FhirServiceAudience if one is not supplied,
    e.g., azurewebsites.net or azurewebsites.us (for US Government cloud)
    .PARAMETER AppRoles
    Names of AppRoles to be defined in the AAD Application registration
    #>
    [CmdletBinding(DefaultParameterSetName='ByFhirServiceName')]
    param(
        [Parameter(Mandatory = $true, ParameterSetName = 'ByFhirServiceName' )]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServiceName,

        [Parameter(Mandatory = $false, ParameterSetName = 'ByFhirServiceAudience' )]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServiceAudience,

        [Parameter(Mandatory = $false, ParameterSetName = 'TenantId' )]
        [ValidateNotNullOrEmpty()]
        [String]$TenantId = "azurewebsites.net",

        [Parameter(Mandatory = $false)]
        [String[]]$AppRoles = "admin"
    )

    Set-StrictMode -Version Latest
    
    # Get current Microsoft Graph context
    try {
        $context = Get-MgContext -ErrorAction Stop
        if (-not $context) {
            throw "No Microsoft Graph session found"
        }
    } 
    catch {
        throw "Please log in to Microsoft Graph with Connect-MgGraph cmdlet before proceeding"
    }

    if ([string]::IsNullOrEmpty($FhirServiceAudience)) {
        $FhirServiceAudience = Get-ServiceAudience -ServiceName $FhirServiceName -TenantId $TenantId
    }

    $desiredAppRoles = @()
    foreach ($role in $AppRoles) {
        $id = New-Guid

        $desiredAppRoles += @{
            AllowedMemberTypes = @("User", "Application")
            Description        = $role
            DisplayName        = $role
            Id                 = $id
            IsEnabled          = "true"
            Value              = $role
        }
    }

    # Create the App Registration using Microsoft Graph
    $appParams = @{
        DisplayName    = $FhirServiceAudience
        IdentifierUris = @($FhirServiceAudience)
        AppRoles       = $desiredAppRoles
        Api            = @{
            Oauth2PermissionScopes = @(@{
                Id                      = [System.Guid]::NewGuid().ToString()
                AdminConsentDescription = "Allow the application to access $FhirServiceAudience on behalf of the signed-in user."
                AdminConsentDisplayName = "Access $FhirServiceAudience"
                IsEnabled               = $true
                Type                    = "User"
                UserConsentDescription  = "Allow the application to access $FhirServiceAudience on your behalf."
                UserConsentDisplayName  = "Access $FhirServiceAudience"
                Value                   = "user_impersonation"
            })
        }
    }
    
    $apiAppReg = New-MgApplication @appParams
    New-MgServicePrincipal -AppId $apiAppReg.AppId | Out-Null

    $tenantId = $context.TenantId
    $authority = "https://login.microsoftonline.com/$tenantId"

    #Return Object
    @{
        AppId     = $apiAppReg.AppId;
        TenantId  = $tenantId;
        Authority = $authority;
        Audience  = $FhirServiceAudience;
    }
}
