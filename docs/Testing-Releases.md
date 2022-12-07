# Testing FHIR Server for Azure Releases

## Description
Changes made to FHIR service are initially released as part of OSS release. After an OSS release has been successfully created, these assets will be staged in the next deployment of FHIR service release.

The deployments for FHIR Service will usually roll out on alternate week after the OSS release, with few exceptions (please see discalimer below). This workflow provides you with an option to run your integration test suite, against the latest OSS release. This is an opportunity to verify or catch issues that impact your service. 

## Understanding the changes in the release
To understand the changes shipped as part of a particular OSS release. Refer to the content under the [releases](https://github.com/microsoft/fhir-server/releases) section.

While the team remains diligent in keeping API compatibility within FHIR versions, as we introduce new features there may be times where changes that impact the API are unintended or unavoidable. To address this, the following labels have been introduced to flag any PRs that have been identified as possibly breaking or changing to API behavior in any way. Use the results of these queries to help determine where to focus testing and analyze the impact to customer system.

- [KI-Warning](https://github.com/microsoft/fhir-server/issues?q=label%3AKI-Warning+) (Known issue: Warning)
- [KI-Breaking](https://github.com/microsoft/fhir-server/issues?q=label%3AKI-Breaking+) (Known issue: Breaking)

**We suggest you to run tests against latest OSS release to catch issues that impact your service.**
 
## How
This guide provides steps to deploy OSS FHIR Server with latest release. 


1. Follow the [steps](https://github.com/microsoft/fhir-server/blob/main/docs/DefaultDeployment.md) to deploy FHIR Service.
2. Verify FHIR server is running.

    Obtain a capability statement from the FHIR server with:

    ```azurecli-interactive
    metadataurl="https://${servicename}.azurewebsites.net/metadata"
    curl --url $metadataurl
    ```

    It will take a minute or so for the server to respond the first time.

3. Run your integration tests. This is the core FHIR Server code that powers the FHIR service and should give you good coverage of API functionality.

4. Clean up resources.

    If you're not going to continue to use this application, delete the resource group with the following steps:

    ```azurecli-interactive
    az group delete --name $servicename
    ```

## What if I find an issue?

You have two options depending on the severity of the issue: 
1. You may open an [issue](https://github.com/microsoft/fhir-server/issues/new/choose) against the Github repository. We actively triage these and will work on this as best effort. 
2. If you have a FHIR account, you can raise a support ticket with concern on upcoming release.


## Additional information

- If you use Azure Pipelines for your CI environment there are examples of creating, testing and removing these environments, including authentication in the [/build/](https://github.com/microsoft/fhir-server/tree/main/build) folder.

- [Quickstart guide for PowerShell](https://github.com/microsoft/fhir-server/blob/main/docs/QuickstartDeployPowerShell.md) contains many of the commands with alternate PS syntax.

## Disclaimer

- The Release cadences discribed here are to inform what our team strives for. There may be factors outside our normal schedule that cause releases to happen more or less frequently. These might include hotfixes, technical issues or other circumstances not mentioned.
- There are other closed source projects and integrations that make Azure API for FHIR possible. This guide does not represent the entire Azure API for FHIR release.
