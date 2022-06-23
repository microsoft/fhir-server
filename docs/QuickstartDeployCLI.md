# Quickstart: Deploy Open Source FHIR server using Azure CLI

In this quickstart, you'll learn how to deploy an Open Source FHIR server in Azure using the Azure CLI.

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## Use Azure Cloud Shell

Azure hosts Azure Cloud Shell, an interactive shell environment that you can use through your browser. You can use either Bash or PowerShell with Cloud Shell to work with Azure services. You can use the Cloud Shell preinstalled commands to run the code in this article without having to install anything on your local environment.

To start Azure Cloud Shell:
1. Select **Try It** in the upper-right corner of a code block. Selecting **Try It** doesn't automatically copy the code to Cloud Shell. 
1. Go to [https://shell.azure.com](https://shell.azure.com), or select the **Launch Cloud Shell** button to open Cloud Shell in your browser. 
1. Select the **Cloud Shell** button on the menu bar at the upper right in the [Azure portal](https://portal.azure.com). Choose to run in Bash mode.

To run the code in this article in Azure Cloud Shell:

1. Start Cloud Shell.
1. Select the **Copy** button on a code block to copy the code.
1. Paste the code into the Cloud Shell session by selecting **Ctrl**+**Shift**+**V** on Windows and Linux or by selecting **Cmd**+**Shift**+**V** on macOS.
1. Select **Enter** to run the code.

## Create resource group

Pick a name for the resource group that will contain the provisioned resources and create it:

(Note: this name must be globally unique to avoid DNS collision with other App Service deployments. For testing purposes, try prepending a descriptive name like `FhirService` with your intials and the date, e.g. `abMay1`)
```azurecli-interactive
servicename="abMay1FhirService"
az group create --name $servicename --location westus
```

## Deploy template

The Microsoft FHIR Server for Azure [GitHub Repository](https://github.com/Microsoft/fhir-server) contains a template that will deploy all necessary resources.<br />

Deploy using CosmosDB as the data store with the following command:

```azurecli-interactive
az deployment group create -g $servicename --template-uri https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json --parameters serviceName=$servicename
```

\
Alternatively, to deploy using SQL Server as the data store: 

```azurecli-interactive
az deployment group create -g $servicename --template-uri https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json --parameters serviceName=$servicename solutionType=FhirServerSqlServer sqlSchemaAutomaticUpdatesEnabled=auto sqlAdminPassword=<replace me>
```

(Note: ensure that your SQL admin password meets the minimum [policy requirements](https://docs.microsoft.com/en-us/sql/relational-databases/security/password-policy?view=sql-server-ver15#password-complexity) to avoid deployment errors)

## Verify FHIR server is running

Obtain a capability statement from the FHIR server with:

```azurecli-interactive
metadataurl="https://${servicename}.azurewebsites.net/metadata"
curl --url $metadataurl
```

It will take a minute or so for the server to respond the first time.

## Clean up resources

If you're not going to continue to use this application, delete the resource group with the following steps:

```azurecli-interactive
az group delete --name $servicename
```

## Next steps

In this tutorial, you've deployed the Microsoft Open Source FHIR Server for Azure into your subscription. To learn how to access the FHIR API using Postman, you can take a look at the [Postman tutorial](https://docs.microsoft.com/en-us/azure/healthcare-apis/access-fhir-postman-tutorial) on the Azure Docs site.
