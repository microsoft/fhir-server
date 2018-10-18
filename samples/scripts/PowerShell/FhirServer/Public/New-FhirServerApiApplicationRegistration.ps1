function New-FhirServerApiApplicationRegistration {
    <#
    .SYNOPSIS
    Create an AAD Application registration for a FHIR server instance.
    .DESCRIPTION
    Create a new AAD Application registration for a FHIR server instance. 
    A FhirServiceName or FhirServiceAudience must be supplied.
    .EXAMPLE
    New-FhirServerApiApplicationRegistration -FhirServiceName "myfhiservice" 
    .EXAMPLE
    New-FhirServerApiApplicationRegistration -FhirServiceAudience "https://myfhirservice.azurewebsites.net"
    .PARAMETER FhirServiceName
    Name of the FHIR service instance. 
    .PARAMETER FhirServiceAudience
    Full URL of the FHIR service.
    .PARAMETER WebAppSuffix
    Will be appended to FHIR service name to form the FhirServiceAudience if one is not supplied,
    e.g., azurewebsites.net or azurewebsites.us (for US Government cloud)
    #>
    [CmdletBinding(DefaultParameterSetName='ByFhirServiceName')]
    param(
        [Parameter(Mandatory = $true, ParameterSetName = 'ByFhirServiceName' )]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServiceName,

        [Parameter(Mandatory = $true, ParameterSetName = 'ByFhirServiceAudience' )]
        [ValidateNotNullOrEmpty()]
        [string]$FhirServiceAudience,

        [Parameter(Mandatory = $false, ParameterSetName = 'ByFhirServiceName' )]
        [String]$WebAppSuffix = "azurewebsites.net"
    )

    Set-StrictMode -Version Latest
    
    # Get current AzureAd context
    try {
        Get-AzureADCurrentSessionInfo -ErrorAction Stop | Out-Null
    } 
    catch {
        throw "Please log in to Azure AD with Connect-AzureAD cmdlet before proceeding"
    }

    if ([string]::IsNullOrEmpty($FhirServiceAudience)) {
        $FhirServiceAudience = "https://$FhirServiceName.$WebAppSuffix}"
    }

    # Create the App Registration
    $apiAppReg = New-AzureADApplication -DisplayName $FhirServiceAudience -IdentifierUris $FhirServiceAudience
    New-AzureAdServicePrincipal -AppId $apiAppReg.AppId | Out-Null

    $aadEndpoint = (Get-AzureADCurrentSessionInfo).Environment.Endpoints["ActiveDirectory"]
    $aadTenantId = (Get-AzureADCurrentSessionInfo).Tenant.Id.ToString()

    #Return Object
    @{
        AppId     = $apiAppReg.AppId;
        TenantId  = $aadTenantId;
        Authority = "$aadEndpoint$aadTenantId";
        Audience  = $FhirServiceAudience;
    }
}
