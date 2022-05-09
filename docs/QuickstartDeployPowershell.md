# Quickstart: Deploy Open Source FHIR server using PowerShell

In this quickstart, learn how to deploy the Open Source Microsoft FHIR server for Azure Using PowerShell.

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

> **NOTE:**
> This article has been updated to use the new Azure PowerShell Az
> module. You can still use the AzureRM module, which will continue to receive bug fixes until at least December 2020.
> To learn more about the new Az module and AzureRM compatibility, see
> [Introducing the new Azure PowerShell Az module](https://docs.microsoft.com/powershell/azure/new-azureps-module-az?view=azps-3.3.0). For
> Az module installation instructions, see [Install Azure PowerShell](https://docs.microsoft.com/powershell/azure/install-az-ps?view=azps-3.3.0).

## Use Azure Cloud Shell

Azure hosts Azure Cloud Shell, an interactive shell environment that you can use through your browser. You can use either Bash or PowerShell with Cloud Shell to work with Azure services. You can use the Cloud Shell preinstalled commands to run the code in this article without having to install anything on your local environment.

To start Azure Cloud Shell:
1. Select **Try It** in the upper-right corner of a code block. Selecting **Try It** doesn't automatically copy the code to Cloud Shell. 
1. Go to [https://shell.azure.com](https://shell.azure.com), or select the **Launch Cloud Shell** button to open Cloud Shell in your browser. 
1. Select the **Cloud Shell** button on the menu bar at the upper right in the [Azure portal](https://portal.azure.com).

To run the code in this article in Azure Cloud Shell:

1. Start Cloud Shell.
1. Select the **Copy** button on a code block to copy the code.
1. Paste the code into the Cloud Shell session by selecting **Ctrl**+**Shift**+**V** on Windows and Linux or by selecting **Cmd**+**Shift**+**V** on macOS.
1. Select **Enter** to run the code.

## Create a resource group

Pick a name for the resource group that will contain the provisioned resources and create it:

(Note: this name must be globally unique to avoid DNS collision with other App Service deployments. For testing purposes, try prepending a descriptive name like `FhirService` with your intials and the date, e.g. `abMay1`)
```azurepowershell-interactive
$fhirServiceName = "abMay1FhirService"
$rg = New-AzResourceGroup -Name $fhirServiceName -Location westus
```

## Deploy the FHIR server template

The Microsoft FHIR Server for Azure [GitHub Repository](https://github.com/Microsoft/fhir-server) contains a template that will deploy all necessary resources. 

Deploy using CosmosDB as the data store with the following command:

```azurepowershell-interactive
New-AzResourceGroupDeployment -TemplateUri https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json -ResourceGroupName $rg.ResourceGroupName -serviceName $fhirServiceName
```

\
Alternatively, to deploy using SQL Server as the data store: 

```azurecli-interactive
$sqlAdminPassword = ConvertTo-SecureString "mySecretPassword123" -AsPlainText -Force
New-AzResourceGroupDeployment -TemplateUri https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json -ResourceGroupName $rg.ResourceGroupName -serviceName $fhirServiceName -solutionType FhirServerSqlServer -sqlSchemaAutomaticUpdatesEnabled auto -sqlAdminPassword $sqlAdminPassword
```

(Note: ensure that your SQL admin password meets the minimum [policy requirements](https://docs.microsoft.com/en-us/sql/relational-databases/security/password-policy?view=sql-server-ver15#password-complexity) to avoid deployment errors)


## Verify FHIR server is running

```azurepowershell-interactive
$metadataUrl = "https://" + $fhirServiceName + ".azurewebsites.net/metadata" 
$metadata = Invoke-WebRequest -Uri $metadataUrl
$metadata.RawContent
```

It will take a minute or so for the server to respond the first time.

## Clean up resources

If you're not going to continue to use this application, delete the resource group
with the following steps:

```azurepowershell-interactive
Remove-AzResourceGroup -Name $rg.ResourceGroupName
```

## Next steps
In this tutorial, you've deployed the Microsoft Open Source FHIR Server for Azure into your subscription. To learn how to access the FHIR API using Postman, you can take a look at the [Postman tutorial](https://docs.microsoft.com/en-us/azure/healthcare-apis/access-fhir-postman-tutorial) on the Azure Docs site.
