# Installation via ARM Template
This article goes through the provisioning and installation of the FHIR server for Azure using an ARM template.

## ARM Template Provisioning
An ARM Template (/src/BaseARMTemplate.json) is provided for easy provisioning of an environment within Azure. When executed, the ARM template will provision the following:

* Azure Web App - Used for hosting the web application. It will have an environment variable for the Key Vault endpoint so that it can bootstrap the Azure Cosmos DB connection. It will make use of Managed Service Identity (MSI) for access to the Key Vault.
* Azure Cosmos DB - Used for the default storage layer for the server. The endpoint URL and primary key are stored within Key Vault.
* Azure Key Vault - Used for secret storage of the Azure Cosmos DB connection information. Permissions for get and list of secrets are granted to the MSI of the Azure Web App

### Prerequisites
To run this ARM template the following additional items must be set up before execution:

* App Service Plan - the service plan for use by the Azure Web App

### Parameters
The following parameters are provided by the ARM template:

|Parameter|Use|
|---|---|
|serviceName|Name used for the resources provisioned. The name is also used in the endpoints for the resources. The value must be between 3-24 alphanumeric characters, begin with a letter, end with a letter or digit, and not contain consecutive hyphens.|
|appServicePlanResourceGroup|The resource group that contains the app service plan. If this is not provided, a new app service plan will be created using the supplied information.|
|appServicePlanName|The app service plan that will host the Azure Web App|
|appServicePlanSku|The sku to use for the app service plan if one is created.|
|additionalKeyVaultPrincipalTenantId|The tenant id of the additional service principal. This is also known as the Directory ID on the properties of the Azure Active Directory. If this parameter is supplied, `additionalKeyVaultObjectId` must also be supplied |
|additionalKeyVaultObjectId|The objectId of the additional service principal|

Example with existing app service plan usage:
```
$rg = New-AzureRmResourceGroup -Name myfhirservicegroup -Location westus2
New-AzureRmResourceGroupDeployment -TemplateFile .\BaseARMTemplate.json -Name fhirdeploy -ResourceGroupName $rg.ResourceGroupName -appServicePlanResourceGroup existingresourcegroup -appServicePlanName existingappserviceplan -serviceName myfhirservicename -additionalKeyVaultPrincipalTenantId 75a98500-3b69-4d1d-9d9e-ff9e9416c5f0 -additionalKeyVaultObjectId 78103636-670a-474e-9937-a8058291b319
```

Example with new app service plan usage:
```
$rg = New-AzureRmResourceGroup -Name myfhirservicegroup -Location westus2
New-AzureRmResourceGroupDeployment -TemplateFile .\BaseARMTemplate.json -Name fhirdeploy -ResourceGroupName $rg.ResourceGroupName -appServicePlanName myfhirservicename -appServicePlanSku S1 -serviceName myfhirservicename -additionalKeyVaultPrincipalTenantId 75a98500-3b69-4d1d-9d9e-ff9e9416c5f0 -additionalKeyVaultObjectId 78103636-670a-474e-9937-a8058291b319
```