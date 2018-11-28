# Microsoft Open Source FHIR Server Deployment

This document describes how to deploy the [Microsoft Open Source FHIR Server](https://github.com/Microsoft/fhir-server). The deployment is a two-step process:

1. (Optional) Create and Azure Active Directory (AAD) Application registration to secure access to the FHIR server. 
2. Deploy Cosmos DB, Azure Web App, and source code using an Azure Resource Manager template. 

The following instructions will be using PowerShell. You can also follow the [Instructions for Azure CLI and Bash](BashDeployment.md).

## Azure Active Directory (AAD) Application Registration

The FHIR server supports token based (JWT) authorization. If authorization is enabled, a client (e.g. a user) accessing the server must present a token from a specified authority and with a specified audience. 

To use the FHIR server with AAD authentication, two Azure Active Directory (AAD) Applications must be registered, one for the FHIR server itself and one for each client accessing the FHIR server. Please refer to the AAD documentation for details on the [Web application to Web API scenario](https://docs.microsoft.com/en-us/azure/active-directory/develop/authentication-scenarios#web-application-to-web-api).

Both AAD Applications can be registered using the Azure Portal. Please refer to the [AAD Application registration documentation](https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-v1-integrate-apps-with-azure-ad) and the [portal application registration notes in this repository](PortalAppRegistration.md).

You can also register the AAD Applications using the [AzureAD PowerShell module](https://docs.microsoft.com/en-us/powershell/module/azuread/). This repository includes a PowerShell module with some wrapper functions that help with the AAD Application registration process.

First import the module with the wrapper functions:

```PowerShell
Import-Module .\samples\scripts\PowerShell\FhirServer\FhirServer.psd1
```

Register an AAD Application for the FHIR server API:

```PowerShell
$fhirServiceName = "myfhirservice"
$apiAppReg = New-FhirServerApiApplicationRegistration -FhirServiceName $fhirServiceName -AppRoles admin,nurse,patient
```

The `-AppRoles` defines a set of roles that can be granted to users or service principals (service accounts) interacting with the FHIR server API. Configuration settings for the FHIR server will determine which privileges (Read, Write, etc.) that are assosiated with each role. 

To access the FHIR server from a client, you will also need a client AAD Application registration with a client secret. This client AAD Application registration will need to have appropriate application permissions and reply URLs configured. Here is how to register a client AAD Application for use with [Postman](https://getpostman.com):

```PowerShell
$clientAppReg = New-FhirServerClientApplicationRegistration -ApiAppId $apiAppReg.AppId -DisplayName "myfhirclient" -ReplyUrl "https://www.getpostman.com/oauth2/callback"
```

If you would like a client application to be able to act as a service account, you can assign roles to the client application:

```PowerShell
Set-FhirServerClientAppRoleAssignments -AppId $clientAppReg.AppId -ApiAppId $apiAppReg.AppId -AppRoles admin,patient
```

To assign roles to a specific user in Azure Active Directory:

```PowerShell
Set-FhirServerUserAppRoleAssignments -UserPrincipalName myuser@mydomain.com -ApiAppId $apiAppReg.AppId -AppRoles admin,nurse
```

## Deploying the FHIR Server Template

To deploy the backend Cosmos DB, Azure Web App, and FHIR server code, use the buttons below to deploy through the Azure Portal. If you would like to protect the FHIR API with token authorization, you will need to supply application registration details as described above. 

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoft%2Ffhir-server%2Fmaster%2Fsamples%2Ftemplates%2Fdefault-azuredeploy.json" target="_blank">
    <img src="https://azuredeploy.net/deploybutton.png"/>
</a>

<a href="https://portal.azure.us/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoft%2Ffhir-server%2Fmaster%2Fsamples%2Ftemplates%2Fdefault-azuredeploy.json" target="_blank"> 
    <img src="https://raw.githubusercontent.com/Azure/azure-quickstart-templates/master/1-CONTRIBUTION-GUIDE/images/deploytoazuregov.png">
</a>

The template can also be deployed using PowerShell. Here is an example of how the authorization details from above can be provided:

```PowerShell
$rg = New-AzureRmResourceGroup -Name "RG-NAME" -Location westus2

New-AzureRmResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json" `
-ResourceGroupName $rg.ResourceGroupName ` 
-serviceName $fhirServiceName ` 
-securityAuthenticationAuthority $apiAppReg.Authority ` 
-securityAuthenticationAudience $apiAppReg.Audience
```

To deploy without Authentication/Authorization:

```PowerShell
$rg = New-AzureRmResourceGroup -Name "RG-NAME" -Location westus2

New-AzureRmResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/master/samples/templates/default-azuredeploy.json" `
-ResourceGroupName $rg.ResourceGroupName -serviceName $fhirServiceName
```

The default deployment will have a single role (`admin`) defined. To define more roles when deploying the server, see [details on specifying roles](Roles.md).

You can use [Postman to test the FHIR server](PostmanTesting.md). 

## Clean up Azure AD App Registrations

To remove the AAD Application registrations:

```PowerShell
Remove-FhirServerApplicationRegistration -AppId $clientAppReg.AppId
Remove-FhirServerApplicationRegistration -AppId $apiAppReg.AppId
```

