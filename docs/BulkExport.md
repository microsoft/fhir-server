# Bulk Export

This feature allows data from the FHIR server to be exported. More details can be found in the [spec](https://github.com/HL7/bulk-data/blob/master/spec/export/index.md). The feature is currently turned off by default. To enable the feature, update the `FhirServer:Operations:Export:Enabled` setting to be true.

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

Currently, only system-wide export is supported. Patient compartment is coming shortly. Checking the export status through the URL returned by the location header during the queuing is also supported. Cancelling the actual export job is supported.
