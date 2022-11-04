Import-Module Microsoft.Graph.Users

Select-MgProfile -Name "beta"

Disconnect-MgGraph
<#
.SYNOPSIS
	Associate an Azure AD User with a FHIR Patient.
.DESCRIPTION
	This script will associate an Azure AD User with a FHIR Patient.
.EXAMPLE
	Set-FHIRUser -TenantId K2S0-1234-5678-90AB -UserObjectId 12345678-1234-5678-1234-567812345678 -FHIRId Patient/12345678-1234-5678-1234-567812345678 -AttributeName extensionAttribute1
.PARAMETER TenantId
	The TenantId of the AAD Tenant.
.PARAMETER UserObjectId
	The ObjectId of the Azure AD User.
.PARAMETER FHIRId
	The FHIR Id of the Patient.
.PARAMETER AttributeName
	The name of the extension attribute to be used for the claims mapping policy.
#>

# Get the parameters for TenantId, UserObjectId, and FHIRId
param(
	[Parameter(Mandatory=$true, HelpMessage="The tenant id of the Azure AD tenant")]
	[string]$TenantId,

	[Parameter(Mandatory=$true, HelpMessage="The ObjectId of the Azure AD User")]
	[string]$UserObjectId,

	[Parameter(Mandatory=$true, HelpMessage="The FHIR Id of the Patient")]
	[string]$FHIRId
	[Parameter(Mandatory=$true, HelpMessage="The name of the extension attribute to set")]
	[string]$AttributeName
)

$scopes = @(
"User.ReadWrite.All"
"Directory.ReadWrite.All"
"Directory.AccessAsUser.All"
)

Connect-MgGraph -TenantId $TenantId -ContextScope Process -Scopes $scopes

$params = @{
	OnPremisesExtensionAttributes = @{
		$AttributeName = "$FHIRId"
	}
}

Update-MgUser -UserId $UserObjectId -BodyParameter $params
