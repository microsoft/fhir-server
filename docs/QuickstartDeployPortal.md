# Quickstart: Deploy Open Source FHIR server using Azure portal

In this quickstart, you'll learn how to deploy an Open Source FHIR Server in Azure using the Azure portal.

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## GitHub Open Source repository

Navigate to the [GitHub deployment page](https://github.com/Microsoft/fhir-server/blob/main/docs/DefaultDeployment.md) and locate the "Deploy to Azure" buttons.

Click the deployment button and the Azure portal opens. Note that this will deploy using a custom template. We have an option to deploy using a docker image. If you would like to use this, please find the templates [here](https://github.com/microsoft/fhir-server/tree/main/samples/templates).

## Fill in deployment parameters

You first need to set the deployment parameters. You can create a new resource group or select an existing resource group. The only other required parameter that doesn't have a default value filled in is a name for the service. If deploying a Sql backed service the Sql Admin Password, and selecting if SQL Schema Updats are automatically enabled are required.

>![Custom Deployment Parameters](images/quickstart-oss-portal/deployment-custom-parameters.png)

Below is a table that describes all of the parameters in the custom deployment template.

|Field Name|Required?|Description|
|-|-|-|
|Subscription|**Yes**|Select the subscription where you want to deploy|
|Resource Group|**Yes**|You can create a new resource group or select an existing resource group from the selected subscription|
|Region|**Yes**|The region that you want to deploy|
|Service Name|**Yes**|Name of the FHIR Service Web App (i.e. MyFHIRServer)|
|App Service Plan Resource Group|No|Specify the exact name of your Resource Group where your App Service Plan lives or leave blank if you want to create a new one|
|App Service Plan Name|No|Specifies the name of a new or existing App Service Plan. If you leave this blank, a new one will be created. If you want to use an existing one, you will need to specify the name exactly. Note that an App Service Plan can be either Linux or Windows. The deployment from this page creates a Windows App and so you will need a Windows App Service Plan. If you use the [Docker deployment](https://github.com/microsoft/fhir-server/blob/main/samples/templates/default-azuredeploy-docker.json) you will need a Linux App Service Plan|
|App Service Plan Sku|No|The Sku of App Service Plan to use if a new one is created.
|Security Authenticaiton Authority|No|This is the URL to the Authority that will confirm your OAuth. If you leave this blank, authentication is disabled. One example is https://login.microsoftonline.com/(tenantid).|
|Security Authentication Audience|No|This is the audience for your authentication, for example, https://xxx.azurewebsites.net. It is used to obtain the access token when the server security is enabled.|
|Enable Aad Smart on Fhir Proxy|**Yes**|Set to true to enable the [AAD Smart on FHIR Proxy](https://docs.microsoft.com/en-us/azure/healthcare-apis/use-smart-on-fhir-proxy)|
|Msdeploy Package Url|No|Webdeploy package to use as depoyment code. If blank, the latest code package will be deployed.|
|Deploy Application Insights|**Yes**|If this is set to true, it enables logging|
|Application Insights Location|**Yes**|Location for Application Insights|
|Additional Fhir Server Config Properties|No|You can specify changes to the App Settings json file. Note that you can also make these changes after deployment|
|Solution Type|**Yes**|Chose between deploying a Cosmos DB or SQL backed server|
|Cosmos DB Account Consistency Policy|For Cosmos DB|An object representing the default consistency policy for the Cosmos DB account. See the [Cosmos DB Documentation](https://docs.microsoft.com/en-us/azure/templates/microsoft.documentdb/databaseaccounts#ConsistencyPolicy)|
|Cosmos DB Free Tier|No|Whether to deploy using the Cosmos DB Free Tier Sku. There is a limit of one free Cosmos DB per subscription.|
|Cosmos DB CMK Url|No|The url for a customer managed Cosmos DB encryption key. If not provided a system managed key will be used. The provided key must be from an Azure Key Vault set up as described in the [documentation](https://docs.microsoft.com/en-us/azure/cosmos-db/how-to-setup-cmk#configure-your-azure-key-vault-instance). If an invalid value is given the service will not start.|
|Sql Admin Password|For SQL|This is the admin password for your SQL Server. Note that there are some password requirements that you can read about [here](https://docs.microsoft.com/en-us/sql/relational-databases/security/password-policy?view=sql-server-ver15#password-complexity)|
|Sql Location|For SQL|This is set by default to deploy to the same region as the web application. If you set this to a different application, you will incure charges since your web application will be in one region and your server will be in another|
|Sql Schema Automatic Updates Enabled|**Yes**|Setting this to 'auto' will mean that you will automatically get schema updates. For production, we recommend setting this to 'tool' and using the [Schema Migration Tool](https://github.com/microsoft/fhir-server/blob/main/docs/SchemaMigrationGuide.md)|
|Fhir Version|**Yes**|Select the FHIR version (STU3, R4, or R5) that you want your FHIR Server to be on.|
|Enable Export|No|Determines whether export will be enabled for this FHIR instance. If true, a storage account will be created as part of the deployment. If you set this to false, you can change it to true after deployment and add the storage account value. More info on the storage setting, check [Bulk Export](https://github.com/microsoft/fhir-server/blob/main/docs/BulkExport.md). Note that you will need owner or user-administrator permissions for this. If you encounter any storage permission or role assignment related issue, a quick workaround is that you set the option to false and ask your administrator to help update the setting later.|
|Enable Convert Data|No|Determines whether the convert data operation will be enabled for this FHIR instance.|
|Enable Reindex|No|Determines whether reindex will be enabled for this FHIR instance.|

After filling in the details, you can start the deployment.

## Validate FHIR Server is running

Once the deployment is complete, you can point your browser to `https://SERVICENAME.azurewebsites.net/metadata` to obtain a capability statement. It will take a minute or so for the server to respond the first time.

## Clean up resources

When no longer needed, you can delete the resource group and all related resources. To do so, select the resource group containing the provisioned resources, select **Delete resource group**, then confirm the name of the resource group to delete.
