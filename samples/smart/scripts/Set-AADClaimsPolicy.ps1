<#
.SYNOPSIS
    This script will create a new AAD Claims Mapping Policy.
.DESCRIPTION
    This script will create a new AAD Claims Mapping Policy using the parameters provided.
.EXAMPLE
    Set-AADClaimsPolicy.ps1 -TenantId K2S0-1234-5678-90AB -ExtensionAttributeName extensionAttribute1
.PARAMETER TenantId
    The TenantId of the AAD Tenant.
.PARAMETER ExtensionAttributeName
    The name of the extension attribute to be used for the claims mapping policy.
#>
param(
    [Parameter(Mandatory=$true, HelpMessage="The tenant id of the Azure AD tenant")]
    [string]$TenantId,

    [Parameter(Mandatory=$true, HelpMessage="The name of the extension attribute to set")]
    [string]$ExtensionAttributeName
)

# Loging to Azure using the specified Azure AD tenant
Connect-AzureAD -TenantId $TenantId

New-AzureADPolicy -Definition @('{
    "ClaimsMappingPolicy":
        {
            "Version":1,"IncludeBasicClaimSet":"true", 
            "ClaimsSchema": [{"Source":"user","ID": $ExtensionAttributeName ,"SamlClaimType":"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/fhirUser","JwtClaimType":"fhirUser"}]
        }
}') -DisplayName "FHIRUserClaim" -Type "ClaimsMappingPolicy"