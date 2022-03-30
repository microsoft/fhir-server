# Importer

The Importer is a .net console application that loads data to the FHIR server at high speed. It improves the data loading speed significantly through batch multi-thread processing. The tool is very easy to use. Download the code, update the settings in the app.config file, compile and run.

Note that the Importer works for service APIs with either Cosmos DB and SQL database backend. However, high throughput rates, as noted in the app.config file, can currently be obtained with Cosmos DB backend with scaled up/out resources. The similar throughput rates cannot be obtained with SQL database backend regardless because resources in bundles are processed one by one through the API, which does not deliver the maximum performance with SQL database. 

For SQL database backend, you can use the [bulk import](../../docs/BulkImport.md) ($import) feature for initial data loading.

For more info on other data loading tools and options, check the [documentation](https://docs.microsoft.com/azure/healthcare-apis/fhir/bulk-importing-fhir-data).

## Features

The primary goal of the Importer is to help load data to the FHIR server as quickly as it can. While the app.config file contains detailed information on the settings, it's worth mentioning some key features of the tool.

- Process ndjson files and resources (one resource per line) in each ndjson file in batch parallel operation.
- Support one or multiple endpoints pointing to the same database. 
- Read blobs/files stored in Azure storage container from specific start and end positions. For example, if there are one million files, you can skip 100,000 files, and load the next 50,000 files.
- Define parallel read threads (ReadThreads) and write threads (WriteThreads). Within each read thread, the write threads is the ratio of (WriteThreads / ReadThreads).
- Define batches for read threads (e.g. 19 files in a batch) and write threads (e.g. 100 lines in a batch from an ndjson file).
- Report reads and writes data for a defined period time, e.g. 30 seconds.
- Retry if an error is encountered, e.g. 10 times.

## Scale for performance

The data processing speeds depend on resources of Azure App Service and Cosmos DB RU/s. You can scale up and/or scale out the App service in the app service plan.

## Known Limits

The Importer is kept lightweight and works with the open-source FHIR server with security disabled. However, you can still use the tool when Azure AD based security is enabled. All you need to do is to complete the following steps.

- Complete application registration.
- Grant the client application with access permissions to the FHIR server.
- Add code to get an Azure Active Directory access token.
- Add the access token in your Put request.

For more details on the steps mentioned above, check [documentation here](https://docs.microsoft.com/azure/healthcare-apis/register-application).
