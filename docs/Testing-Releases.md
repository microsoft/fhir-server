# Testing FHIR Server for Azure Releases

## Description

This guide provides information to set up a docker container with the latest release of FHIR Server for Azure so that you can run your own integration tests against the API.

## Why

FHIR Server for Azure works on a release cadence of around every two weeks. You can find information detailing the technical content of the releases under the [releases](https://github.com/microsoft/fhir-server/releases) section in Github.

After a release has been created, these assets will be staged in the next deployment of [Azure API for FHIR](https://azure.microsoft.com/en-us/services/azure-api-for-fhir/). The deployments for Azure API for FHIR will usually roll out the following week after the OSS release, giving your team about a week to test with these incremental releases.

## What to focus on

While the team remains diligent in keeping API compatibility within FHIR versions, as we introduce new features there may be times where changes that impact the API are unintended or unavoidable. To address this, the following labels have been introduced to flag any PRs that have been identified as possibly breaking or changing to API behavior in any way. Use the results of these queries to help determine where to focus testing and analyze the impact to your system.

- [KI-Warning](https://github.com/microsoft/fhir-server/issues?q=label%3AKI-Warning+) (Known issue: Warning)
- [KI-Breaking](https://github.com/microsoft/fhir-server/issues?q=label%3AKI-Breaking+) (Known issue: Breaking)

## What if I find an issue?

You have two options depending on the severity of the issue: 
1. You may open an [issue](https://github.com/microsoft/fhir-server/issues/new/choose) against the Github repository. We actively triage these and will work on this as best effort. 
1. If you have an Azure API for FHIR account, you can also raise a support ticket with concern on the next release.

## How

There are a few different kinds of release assets, both Webdeploy zip files and Docker image. This guide will focus on using the docker images. 

Here are the tags for Docker images that should be noted:

1. mcr.microsoft.com/healthcareapis/${version}-fhir-server:**latest** - This is the image we will focus on.
2. healthplatformregistry.azurecr.io/${version}_fhir-server:**release** - The latest release, if the mcr latest image as mentioned above has issues, consider this.
3. healthplatformregistry.azurecr.io/${version}_fhir-server:**master** - The latest code that has been merged to the main branch and passed all tests.
4. healthplatformregistry.azurecr.io/${version}_fhir-server:**build-[release number]** - Allows you to pin or access a specific release.


The current data store used by Azure API for FHIR is Cosmos DB, the following steps will walk through creating a test environment using Containers targeting the "release" label above.

### Steps

1. To create a test environment for the release, we can leverage the [default-azuredeploy-docker.json](https://github.com/microsoft/fhir-server/blob/main/samples/templates/default-azuredeploy-docker.json) template. 

1. Use the [Quickstart guide](https://github.com/microsoft/fhir-server/blob/main/docs/QuickstartDeployCLI.md) if you are unfamiliar with CloudShell and starting with Azure CLI.

1. Using Azure CLI create a resource group for the test server:

    ```azurecli-interactive
    servicename="latestfhirservice"
    az group create --name $servicename --location westus2
    ```

1. Deploy the latest release (the default image) of the FHIR Server:

    ```azurecli-interactive
    az deployment group create -g $servicename --template-uri https://raw.githubusercontent.com/Microsoft/fhir-server/main/samples/templates/default-azuredeploy-docker.json --parameters serviceName=$servicename
    ```

    Note: We have the ability to pass in the `-imageTag` parameter in the format shown above if we wanted to target a specific release, refer the the [releases page](https://github.com/microsoft/fhir-server/releases). For example `-imageTag build-20200101-1`

1. Verify FHIR server is running.

    Obtain a capability statement from the FHIR server with:

    ```azurecli-interactive
    metadataurl="https://${servicename}.azurewebsites.net/metadata"
    curl --url $metadataurl
    ```

    It will take a minute or so for the server to respond the first time.

1. Run your integration tests. This is the core FHIR Server code that powers the Azure API for FHIR and should give you good coverage of API functionality.

1. Clean up resources.

    If you're not going to continue to use this application, delete the resource group with the following steps:

    ```azurecli-interactive
    az group delete --name $servicename
    ```


As all the steps above use the CLI, this process could be scripted into a CI environment and run on a desired cadence. 

## Additional information

- If you use Azure Pipelines for your CI environment there are examples of creating, testing and removing these environments, including authentication in the [/build/](https://github.com/microsoft/fhir-server/tree/main/build) folder.

- [Quickstart guide for PowerShell](https://github.com/microsoft/fhir-server/blob/main/docs/QuickstartDeployPowerShell.md) contains many of the commands with alternate PS syntax.

## Disclaimer

- The Release cadences discribed here are to inform what our team strives for. There may be factors outside our normal schedule that cause releases to happen more or less frequently. These might include hotfixes, technical issues or other circumstances not mentioned.
- There are other closed source projects and integrations that make Azure API for FHIR possible. This guide does not represent the entire Azure API for FHIR release.
