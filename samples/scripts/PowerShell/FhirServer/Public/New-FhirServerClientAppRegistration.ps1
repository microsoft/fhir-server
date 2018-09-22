function New-FhirServerClientAppRegistration {

param(
    [Parameter(Mandatory = $true)]
    [string]$ApiAppId,

    [Parameter(Mandatory = $true)]
    [string]$DisplayName,

    [Parameter(Mandatory = $false)]
    [string]$ReplyUrl = "https://www.getpostman.com/oauth2/callback",

    [Parameter(Mandatory = $false)]
    [string]$IdentifierUri
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

if ([string]::IsNullOrEmpty($IdentifierUri))
{
    $IdentifierUri = "https://${DisplayName}"
}

$apiAppReg = Get-AzureADApplication -Filter "AppId eq '${ApiAppId}'"

# Required App permission for Azure AD sign-in
$reqAad = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
$reqAad.ResourceAppId = "00000002-0000-0000-c000-000000000000"
$reqAad.ResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList "311a71cc-e848-46a1-bdf8-97ff7156d8e6","Scope"

# Required App Permission for the API application registration. 
$reqApi = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
$reqApi.ResourceAppId = $apiAppReg.AppId #From API App registration above

# Just add the first scope (user impersonation)
$reqApi.ResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" -ArgumentList $apiAppReg.Oauth2Permissions[0].id,"Scope"

$clientAppReg = New-AzureADApplication -DisplayName $DisplayName -IdentifierUris $IdentifierUri -RequiredResourceAccess $reqAad,$reqApi -ReplyUrls $ReplyUrl

# Create a client secret
$clientAppPassword = New-AzureADApplicationPasswordCredential -ObjectId $clientAppReg.ObjectId

# Create Service Principal
$ignored = New-AzureAdServicePrincipal -AppId $clientAppReg.AppId

$securityAuthenticationAudience = $apiAppReg.IdentifierUris[0]
$aadEndpoint = (Get-AzureADCurrentSessionInfo).Environment.Endpoints["ActiveDirectory"]
$aadTenantId = (Get-AzureADCurrentSessionInfo).Tenant.Id.ToString()
$securityAuthenticationAuthority = "${aadEndpoint}${aadTenantId}"

@{
    AppId = $clientAppReg.AppId;
    AppSecret = $clientAppPassword.Value;
    ReplyUrl = $clientAppReg.ReplyUrls[0]
    AuthUrl = "${securityAuthenticationAuthority}/oauth2/authorize?resource=${securityAuthenticationAudience}"
    TokenUrl = "${securityAuthenticationAuthority}/oauth2/token"
}

}