# Azure SMART on FHIR ONC (g)(10) Sample

Sample for ONC (g)(10) / SMART on FHIR work. Still under active development.

## Prerequisites

This samples uses the Azure Developer CLI. Please install this via [the instructions here](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd?tabs=baremetal%2Cwindows).

Open a terminal to the same directory as this README.

Make sure to login to the correct tenant with the `az cli`. For example:

`az login -t 12345678-90ab-cdef-1234-567890abcdef`

## Development Environments

## Creating a new environment

To create a new development environment, run `azd init` with the following parameters:

- name: environment name from above
- location: centralus
- subscription: your-subscription

Run `azd up` to deploy the infra and function app.

### Working with an active environment

To change environments, run `azd env select`.

If you need to change the Azure resources in your environment, change the bicep templates in `/infra` and run `azd provision`.

To deploy the Function App to Azure, run `azd deploy`.
