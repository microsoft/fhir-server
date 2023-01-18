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
$apiAppReg = New-FhirServerApiApplicationRegistration -FhirServiceName $fhirServiceName -AppRoles globalAdmin
```

The `-AppRoles` defines a set of roles that can be granted to users or service principals (service accounts) interacting with the FHIR server API. Configuration settings for the FHIR server will determine which privileges (Read, Write, etc.) that are assosiated with each role. The allowed roles are enumerated in [roles.json](../src/Microsoft.Health.Fhir.Shared.Web/roles.json).

To access the FHIR server from a client, you will also need a client AAD Application registration with a client secret. This client AAD Application registration will need to have appropriate application permissions and reply URLs configured. Here is how to register a client AAD Application for use with [Postman](https://getpostman.com):

```PowerShell
$clientAppReg = New-FhirServerClientApplicationRegistration -ApiAppId $apiAppReg.AppId -DisplayName "myfhirclient" -ReplyUrl "https://www.getpostman.com/oauth2/callback"
```

If you would like a client application to be able to act as a service account, you can assign roles to the client application:

```PowerShell
Set-FhirServerClientAppRoleAssignments -AppId $clientAppReg.AppId -ApiAppId $apiAppReg.AppId -AppRoles globalAdmin
```

To assign roles to a specific user in Azure Active Directory:

```PowerShell
Set-FhirServerUserAppRoleAssignments -UserPrincipalName myuser@mydomain.com -ApiAppId $apiAppReg.AppId -AppRoles globalAdmin
```

## Deploying the FHIR Server Template

We recommend starting with the SQL deployment as it offers the most features. However deployments in cosmos are still available. You can find all of our templates [here](https://github.com/microsoft/fhir-server/tree/main/samples/templates). 

Note that when you deploy from our templates, it is set to deploy with $export enabled. As part of the export setup, a storage account is automatically created along with roles. The creation of this storage account and roles requires that you have contributor access at the subscription level. If the person deploying does not have contributor access at the subscription level, the deployment will fail. To deploy successfully in this scenario, you can deploy with export set to false. If you need to use export, it can be configured to true after deployment by someone with contributor access at the subscription level. It takes a few more steps as someone will need to create and link the storage account (it won’t automatically get created) but from there it will work exactly as if you had set it to true at provisioning.

To deploy the backend SQL Server, Azure Web App, and FHIR server code, use the buttons below to deploy through the Azure Portal. If you would like to protect the FHIR API with token authorization, you will need to supply application registration details as described above.

The FHIR server can be deployed using all free Azure resources. When deploying select 'F1' as the App Service Plan Sku, 'Yes' to use the Cosmos DB Free Tier, and FhirServerCosmosDB as the Solution Type. The free app service plan and Cosmos Db account have restrictions that can be seen on their respective doc pages: [App Service plan overview](https://docs.microsoft.com/en-us/azure/app-service/overview-hosting-plans), [Cosmos DB free tier](https://docs.microsoft.com/en-us/azure/cosmos-db/optimize-dev-test#azure-cosmos-db-free-tier)

<a href="https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FMicrosoft%2Ffhir-server%2Fmain%2Fsamples%2Ftemplates%2Fdefault-azuredeploy-docker.json" target="_blank">
    <img src="https://aka.ms/deploytoazurebutton"/>
</a>

You can also deploy using PowerShell. The example below leverages the CosmosDB template. Here is an example of how the authorization details from above can be provided:

```PowerShell
$rg = New-AzResourceGroup -Name "RG-NAME" -Location westus2

New-AzResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json" `
-ResourceGroupName $rg.ResourceGroupName ` 
-serviceName $fhirServiceName ` 
-securityAuthenticationAuthority $apiAppReg.Authority ` 
-securityAuthenticationAudience $apiAppReg.Audience
```

To deploy without Authentication/Authorization:

```PowerShell
$rg = New-AzResourceGroup -Name "RG-NAME" -Location westus2

New-AzResourceGroupDeployment `
-TemplateUri "https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json" `
-ResourceGroupName $rg.ResourceGroupName -serviceName $fhirServiceName
```

The default deployment will have a number of roles as defined in the [roles.json](../src/Microsoft.Health.Fhir.Shared.Web/roles.json) file. To define more roles when deploying the server, see [details on specifying roles](Roles.md).

You can use [Postman to test the FHIR server](PostmanTesting.md). 

## Clean up Azure AD App Registrations

To remove the AAD Application registrations:

```PowerShell
Remove-FhirServerApplicationRegistration -AppId $clientAppReg.AppId
Remove-FhirServerApplicationRegistration -AppId $apiAppReg.AppId
```
