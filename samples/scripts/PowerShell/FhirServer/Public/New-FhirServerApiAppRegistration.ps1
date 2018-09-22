function New-FhirServerApiAppRegistration {

param(
    [Parameter(Mandatory = $false)]
    [string]$FhirServiceName,

    [Parameter(Mandatory = $false)]
    [string]$FhirServiceAudience,

    [Parameter(Mandatory = $false)]
    [String]$WebAppSuffix = "azurewebsites.net"
)

# Get current AzureAd context
try {
    $session = Get-AzureADCurrentSessionInfo -ErrorAction Stop
} 
catch 
{
    Write-Host "Please log into Azure AD with Connect-AzureAD cmdlet before proceeding"
    Break
}

if ([string]::IsNullOrEmpty($FhirServiceName) -and [string]::IsNullOrEmpty($FhirServiceAudience))
{
    Write-Host "Please provide either a FhirServiceName or a FhirServiceAudience"
    Break
}

if ([string]::IsNullOrEmpty($FhirServiceAudience))
{
    $FhirServiceAudience = "https://${FhirServiceName}.${WebAppSuffix}"
}

# Create the App Registration
$apiAppReg = New-AzureADApplication -DisplayName $FhirServiceAudience -IdentifierUris $FhirServiceAudience
$ignored = New-AzureAdServicePrincipal -AppId $apiAppReg.AppId

$aadEndpoint = (Get-AzureADCurrentSessionInfo).Environment.Endpoints["ActiveDirectory"]
$aadTenantId = (Get-AzureADCurrentSessionInfo).Tenant.Id.ToString()

#Return Object
@{
    AppId = $apiAppReg.AppId;
    TenantId = $aadTenantId;
    Authority = "${aadEndpoint}${aadTenantId}";
    Audience = $FhirServiceAudience;
}

}