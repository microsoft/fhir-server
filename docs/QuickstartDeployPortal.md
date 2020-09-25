# Quickstart: Deploy Open Source FHIR server using Azure portal

In this quickstart, you'll learn how to deploy an Open Source FHIR Server in Azure using the Azure portal.

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## GitHub Open Source repository

Navigate to the [GitHub deployment page](https://github.com/Microsoft/fhir-server/blob/master/docs/DefaultDeployment.md) and locate the "Deploy to Azure" buttons:

>![Open Source Deployment Page](images/quickstart-oss-portal/deployment-page-oss.png)

Click the deployment button and the Azure portal opens.

## Fill in deployment parameters

Choose to create a new resource group and give it a name or select an existing resource group. Only other required parameters are a name for the service, the Sql Admin Password, and to select if SQL Schema Updats are automatically enabled.

>![Custom Deployment Parameters](images/quickstart-oss-portal/deployment-custom-parameters.png)

Below is a table that describes all of the parameters in the custom deployment template.

|Field Name|Required?|Description|Default Value|
|-|-|-|
|Subscription|**Yes**|Select the subscription where you want to deploy|
|Resource Group|**Yes**|You can create a new resource group or select an existing resource group from the selected subscription|
|Region|**Yes**|The region that you want to deploy|
|Service Name|**Yes**|Name of the FHIR Service Web App (i.e. MyFHIRServer)|
|App Service Plan Resource Group|No|Specify the exact name of your App Service Plan Resource where your App Service Plan lives or leave blank if you want to create a new one|
|App Service Plan Name|No|If you leave this blank, a new one will be created. If you want to use an existing one, you will need to specify the name exactly. It needs to be in the same region that you are deploying and needs to match in type (either Linux or Windows)|
|Security Authenticaiton Authority|No||
|Security Authentication Audience|||
|Enable Aad Smart on Fhir Proxy|**Yes**|Set to true to enable the [AAD Smart on FHIR Proxy](https://docs.microsoft.com/en-us/azure/healthcare-apis/use-smart-on-fhir-proxy)|
|Msdeploy Package Url|No|Webdeploy package to use as depoyment code. If blank, the latest code package will be deployed.|
|Deploy Application Insights|**Yes**||
|Application Insights Location|**Yes**||
|Additional Fhir Server Config Properties|||
|Solution Type|||
|Sql Admin Password|Yes||
|Sql Location|No||
|Fhir Version|No|Select the FHIR version (STU3, R4, or R5) that you want your FHIR Server to be on|
|Enable Export|No|Determines whether export will be enabled for this fhir instance. If true, a storage account will be created as part of the deployment. You will need owner or user-administrator permissions for this.|
|Sql Schema Automatic Updates Enabled|||

After filling in the details, you can start the deployment.

## Validate FHIR Server is running

Once the deployment is complete, you can point your browser to `https://SERVICENAME.azurewebsites.net/metadata` to obtain a capability statement. It will take a minute or so for the server to respond the first time.

## Clean up resources

When no longer needed, you can delete the resource group and all related resources. To do so, select the resource group containing the provisioned resources, select **Delete resource group**, then confirm the name of the resource group to delete.
