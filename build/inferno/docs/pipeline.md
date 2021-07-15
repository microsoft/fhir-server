# Azure DevOps pipeline

We provide an [automated AzDO pipeline](.azure-pipelines/inferno-test.yml) which spins up a FHIR instance (containerized), SQL server and execute the inferno tests against FHIR.

## Steps

The pipeline comprise from several steps:

1. Cloning the Inferno project repository
1. Spinning up FHIR server + SQL server using docker compose
1. Downloading a known FHIR dataset, [changing data format](docs/import-test-data.md) and importing it into the FHIR server
1. Preparing the Inferno configuration files
1. Running the Inferno tests

## Importing the pipeline

Create a new AzDO pipeline and refer it to the [yaml definition file](.azure-pipelines/inferno-test.yml).
Define the needed variables:

1. if SMART on FHIR capabilities are supported / required to be tested the full list of required variables can be found in the 'inject args' task in the [yaml file](.azure-pipelines/inferno-test.yml).
1. if only the FHIR core functionality is required. only the 'SERVER' 'FHIR_ENDPOINT_INSIDE_DOCKER' and 'FHIR_ENDPOINT_FROM_HOST' variables are required.