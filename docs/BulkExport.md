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

For more details on Bulk Export, see the [Azure API for FHIR Export Data page](https://docs.microsoft.com/azure/healthcare-apis/export-data). This page includes details on support for Bulk Export in our managed service and all features in the managed service are available in open source.

In addition, $export supports the _format_ parameter which allows a user to select a format for the file structure that the export job creates. Different formats can be defined in the appSettings by combining constants, folder level breaks ('/') and known tags. The tags will be replaced with data when the job is run. The three supported tags are **resourcename**, **timestamp**, and **id**:
* resourcename: Replaced with the resource type being exported (i.e. Patient)
* timestamp: Replaced with a timestamp of the job's queried time
* id: Replaced with the GUID of the export job. 

Adds a setting for configuring the file structure format for export jobs.
