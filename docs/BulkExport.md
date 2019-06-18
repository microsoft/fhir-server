# Bulk Export

This feature allows data from the FHIR server to be exported. More detail can be found in the [spec](https://github.com/HL7/bulk-data/blob/master/spec/export/index.md).

The feature is currently under preview and is turned off by default. To enable the feature, update the `FhirServer:Operations:Export:Enabled` setting to be true.

## Specifying destination

One customization that we have done in addition to the spec is _requiring_ the destination information to be supplied. This is a breaking change from the spec. We are planning to support default destination to maintain compatibility with spec.

The destination type can be specified by using `_destinationType` query parameter and any destination specific settings can be specified as base64 encoded string by using `_destinationConnectionSettings` query parameter.

Currently, we only support Azure Blob storage as the destination. This can be specified with `_destinationType=azure-block-blob` and `_destinationConnectionSettings=[base64 encoding of the connection string]`.

We recommend to use supply connection string with SAS token so that the token can expire after certain period of time.

Example of connection string with SAS token:

```
BlobEndpoint=https://example.blob.core.windows.net/;QueueEndpoint=https://example.queue.core.windows.net/;FileEndpoint=https://example.file.core.windows.net/;TableEndpoint=https://example.table.core.windows.net/;SharedAccessSignature=[SAS Token]
```

Base64 encoding of the connection string with SAS token:

```
QmxvYkVuZHBvaW50PWh0dHBzOi8vZXhhbXBsZS5ibG9iLmNvcmUud2luZG93cy5uZXQvO1F1ZXVlRW5kcG9pbnQ9aHR0cHM6Ly9leGFtcGxlLnF1ZXVlLmNvcmUud2luZG93cy5uZXQvO0ZpbGVFbmRwb2ludD1odHRwczovL2V4YW1wbGUuZmlsZS5jb3JlLndpbmRvd3MubmV0LztUYWJsZUVuZHBvaW50PWh0dHBzOi8vZXhhbXBsZS50YWJsZS5jb3JlLndpbmRvd3MubmV0LztTaGFyZWRBY2Nlc3NTaWduYXR1cmU9W1NBUyBUb2tlbl0=
```

Example of the URL used to queue a new job using the destination:

```
https://test-fhir-server/$export?_destinationType=azure-block-blob&_destinationConnectionSettings=QmxvYkVuZHBvaW50PWh0dHBzOi8vZXhhbXBsZS5ibG9iLmNvcmUud2luZG93cy5uZXQvO1F1ZXVlRW5kcG9pbnQ9aHR0cHM6Ly9leGFtcGxlLnF1ZXVlLmNvcmUud2luZG93cy5uZXQvO0ZpbGVFbmRwb2ludD1odHRwczovL2V4YW1wbGUuZmlsZS5jb3JlLndpbmRvd3MubmV0LztUYWJsZUVuZHBvaW50PWh0dHBzOi8vZXhhbXBsZS50YWJsZS5jb3JlLndpbmRvd3MubmV0LztTaGFyZWRBY2Nlc3NTaWduYXR1cmU9W1NBUyBUb2tlbl0=
```

## What is supported

Currently, only system-wide export is supported. Patient compartment is coming shortly. Checking the export status through the URL returned by the location header during the queuing is also supported. Cancelling the actual export job is currently not supported and will be supported soon.
