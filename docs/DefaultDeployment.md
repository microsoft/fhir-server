Microsoft Open Source FHIR Server Deployment
============================================

This document describes how to deploy the [Microsoft Open Source FHIR Server](https://github.com/Microsoft/fhir-server). The deployment is a two-step process:

1. (Optional) Create and Azure Active Directory (AAD) application registration to secure access to the FHIR server. 
2. Deploy Cosmos DB, Azure Web App, and source code using an Azure Resource Manager template. 

Azure Active Directory Application Registration
-----------------------------------------------

The FHIR server supports token based (JWT) authorization. If authorization is enabled, a user or an application accessing the server must present a token from a specified authority and with a specified audience. 

To use the FHIR server, two Azure Active Directory applications must be registered, one for the FHIR server itself and one for a client application accessing the FHIR server. Please refer to the Azure Active Directory documentation for details on the [Web application to Web API scenario](https://docs.microsoft.com/en-us/azure/active-directory/develop/authentication-scenarios#web-application-to-web-api).

Both Azure Active Directory applications can be registered using the Azure Portal. Please refer to the [Azure Active Directory application registration documentation](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-v1-integrate-apps-with-azure-ad).

You can also register the applications using the [AzureAD PowerShell module](https://docs.microsoft.com/en-us/powershell/module/azuread/). First register an app for the FHIR server API:

```PowerShell
# Make sure you are connected to Azure AD:
Connect-AzureAd

# FHIR audience URL. 
# A good choice is the URL of your FHIR service, e.g:
$fhirServiceName = "myfhirservice"

# Note: use "https://${fhirServiceName}.azurewebsites.us" for US Government
$fhirApiAudience = "https://${fhirServiceName}.azurewebsites.net"

# Create the App Registration:
$apiAppReg = New-AzureADApplication -DisplayName $fhirApiAudience -IdentifierUris $fhirApiAudience
New-AzureAdServicePrincipal -AppId $apiAppReg.AppId

# Gather some information for the deployment:
$aadEndpoint = (Get-AzureADCurrentSessionInfo).Environment.Endpoints["ActiveDirectory"]
$aadTenantId = (Get-AzureADCurrentSessionInfo).Tenant.Id.ToString()

$securityAuthenticationAuthority = "${aadEndpoint}${aadTenantId}"
$securityAuthenticationAudience = $fhirApiAudience

# Display deployment information
Write-Host @"
FHIR Service Name (serviceName): ${fhirServiceName}
Authority (securityAuthenticationAuthority): ${securityAuthenticationAuthority}
Audience (securityAuthenticationAudience): ${securityAuthenticationAudience}
"@
```

To access the FHIR server from a client application, you will also need a client app registration with a client secret. This client application registration will need to have appropriate application permissions and reply URLs configured. Here is how to register a client app for use with [Postman](https://getpostman.com):

```PowerShell
# First specify which permissions this app should have.

# Required App permission for Azure AD sign-in
$reqAad = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
$reqAad.ResourceAppId = "00000002-0000-0000-c000-000000000000"
$reqAad.ResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" `
-ArgumentList "311a71cc-e848-46a1-bdf8-97ff7156d8e6","Scope"

# Required App Permission for the API application registration. 
$reqApi = New-Object -TypeName "Microsoft.Open.AzureAD.Model.RequiredResourceAccess"
$reqApi.ResourceAppId = $apiAppReg.AppId #From API App registration above

# Just add the first scope (user impersonation)
$reqApi.ResourceAccess = New-Object -TypeName "Microsoft.Open.AzureAD.Model.ResourceAccess" `
-ArgumentList $apiAppReg.Oauth2Permissions[0].id,"Scope"

# Application registration for API
$postmanReplyUrl="https://www.getpostman.com/oauth2/callback"

$clientAppReg = New-AzureADApplication -DisplayName "${fhirServiceName}-postman" `
-IdentifierUris "https://${fhirServiceName}-postman" `
-RequiredResourceAccess $reqAad,$reqApi -ReplyUrls $postmanReplyUrl

# Create a client secret
$clientAppPassword = New-AzureADApplicationPasswordCredential -ObjectId $clientAppReg.ObjectId

# Create Service Principal
New-AzureAdServicePrincipal -AppId $clientAppReg.AppId

# Write some information needed to use client app
$clientId = $clientAppReg.AppId
$clientSecret = $clientAppPassword.Value
$replyUrl = $clientAppReg.ReplyUrls[0]
$authUrl = "${securityAuthenticationAuthority}/oauth2/authorize?resource=${securityAuthenticationAudience}"
$tokenUrl = "${securityAuthenticationAuthority}/oauth2/token"

Write-Host @"  
=== Settings for Postman OAuth2 authentication ===

   Callback URL: ${replyUrl}
   Auth URL: ${authUrl}
   Access Token URL: ${tokenUrl}
   Client ID: ${clientId}
   Client Secret: ${clientSecret}

"@
```

Deploying the FHIR Server Template
----------------------------------

To deploy the backend Cosmos DB, Azure Web App, and FHIR server code, use the buttons below to deploy through the Azure Portal. If you would like to protect the FHIR API with token authorization, you will need to supply application registration details as described above. 

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoft%2Ffhir-server%2Fmaster%2Fsamples%2Ftemplates%2Fdefault-azuredeploy.json" target="_blank">
    <img src="https://azuredeploy.net/deploybutton.png"/>
</a>

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoft%2Ffhir-server%2Fmaster%2Fsamples%2Ftemplates%2Fdefault-azuredeploy.json" target="_blank">
    <img src="https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazuregov.png">
</a>

The template can also be deployed using PowerShell. Here is an example of how the authorization details from above can be provided:

```PowerShell
$rg = New-AzureRmResourceGroup -Name "RG-NAME" -Location westus2

New-AzureRmResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json" `
-ResourceGroupName $rg.ResourceGroupName -serviceName $fhirServiceName `
-securityAuthenticationAuthority $securityAuthenticationAuthority `
-securityAuthenticationAudience $securityAuthenticationAudience
```

To deploy without Authentication/Authorization:

```PowerShell
$rg = New-AzureRmResourceGroup -Name "RG-NAME" -Location westus2

New-AzureRmResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json" `
-ResourceGroupName $rg.ResourceGroupName -serviceName $fhirServiceName
```

Testing FHIR Server with Postman
--------------------------------

You can use [Postman](https://getpostman.com) to test the FHIR server. If you have deployed with Authentication/Authorization enabled, you will need to [configure Azure AD authorization](https://blog.jongallant.com/2017/03/azure-active-directory-access-tokens-postman/
) to obtain a token. Use type "OAuth 2.0" and set the following parameters:

*Grant Type*: `Auhorization Code`

*Callback URL*: `https://www.getpostman.com/oauth2/callback`

*Auth URL*: `https://login.microsoftonline.com/{TENANT-ID}/oauth2/authorize?resource={AUDIENCE}`

*Access Token URL*: `https://login.microsoftonline.com/{TENANT-ID}/oauth2/token`

*Client ID*: `CLIENT-APP-ID` (see above)

*Client Secret*: `SECRET` (see above)

*Scope*: `Ignored` (not used for Azure AD v1.0 endpoints)

*State*: e.g., `12345`

*Client Authentication*: `Send client credentials in body`

Verify that the following requests return status `200 OK`:

```
GET https://myfhirservice.azurewebsites.net/metadata
GET https://myfhirservice.azurewebsites.net/Patient
```
