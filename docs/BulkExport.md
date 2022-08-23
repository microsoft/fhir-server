# Bulk Export

This feature allows data from the FHIR server to be exported. More details can be found in the [spec](https://hl7.org/fhir/uv/bulkdata/export/index.html). The feature is currently turned on by default. To disable the feature, update the `FhirServer:Operations:Export:Enabled` setting to be false.

## Specifying destination

There are two ways by which one can set the destination storage account to export data to. One way would be to use the connection string for the storage account and update the `FhirServer:Operations:Export:StorageAccountConnection` setting. The fhir-server will use the connection string to connect to the storage account and export data.

The other option would be to use the `FhirServer:Operations:Export:StorageAccountUri` setting with the uri of the storage account. For this option, we assume that the fhir-server has permissions to contribute data to the corresponding storage account. One way to achieve this (assuming you are running the fhir-server code in App Service with Managed Identity enabled) would be to give the App Service `Storage Blob Data Contributor` permissions for the storage account of your choice.

Currently, we only support Azure Blob storage as the destination.

We recommend to use connection string with SAS token so that the token can expire after certain period of time.

Examples of connection string that is expected when using `FhirServer:Operations:Export:StorageAccountConnection`:

```
BlobEndpoint=https://example.blob.core.windows.net/;QueueEndpoint=https://example.queue.core.windows.net/;FileEndpoint=https://example.file.core.windows.net/;TableEndpoint=https://example.table.core.windows.net/;SharedAccessSignature=[SAS Token]
```

Well known Azure Storage Emulator connection string:

```
DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;
```

Example of a storage account uri that is expected when using `FhirServer:Operations:Export:StorageAccountUri`:

```
https://<accountName>.blob.core.windows.net/
```

Example of the URL used to queue a new export job:

```
https://test-fhir-server/$export
```

## What is supported

For more details on Bulk Export, see the [Azure API for FHIR Export Data page](https://docs.microsoft.com/en-us/azure/healthcare-apis/fhir/export-data). 

In addition to the query parameters specified in the Azure API For FHIR documentation, users can also use the \_format in FHIR Server. \_format allows a user to select a format for the file structure that the export job creates. Different formats can be defined in the appSettings by combining constants, folder level breaks ('/'), and known tags. The tags will be replaced with data when the job is run. The three supported tags are: 
* **resourcename**: replaces with the resource type being exported
* **timestamp**: replaces with a timestamp of the job's queried time
* **id**: replaces with the GUID of the export job

To use the format, you will need to set the following settings in the appSettings:

| appSetting | Description | Example Value|
|------------|-------------|--------------|
| FhirServer:Operations:Export:Formats:#:Name | Name of the format you plan to call. The # should be replaced as you can specify multiple formats. We provide default values for 0 and 1 (for when a container is specified and when a container is not specified) so recommend starting with 2 | TestFormat |
| FhirServer:Operations:Export:Formats:#:Format | Defines the format. The # should match the one used above. | test/\<resourcename>/\<id>/\<timestamp> |

In the table above, you would use format in the following way `GET https://<<FHIR service base URL>>/$export?_format=TestFormat`. The result would be an export saved in a folder structure **test/\<resourcename>/\<id>** and the file name would be **\<timestamp>.ndjson**.

Exported data can be deidentified using the [FHIR Tools for Anonymization](https://github.com/microsoft/FHIR-Tools-for-Anonymization#how-to-perform-de-identified-export-operation-on-the-fhir-server)
