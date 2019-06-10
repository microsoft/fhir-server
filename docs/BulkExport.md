# Bulk Export

This feature allows data from the FHIR server to be exported. More detail can be found in the [spec](https://github.com/HL7/bulk-data/blob/master/spec/export/index.md).

The feature is currently under preview and is turned off by default. To enable the feature, update the `FhirServer:Operations:Export:Enabled` setting to be true.

## Specifying destination

One customization that we have done in addition to the spec is requiring the destination information to be supplied. The destination type can be specified by using `_destinationType` query parameter and any destination specific settings can be specified as BASE64 encoded string by using `_destinationConnectionSettings` query parameter.

Currently, we only support Azure Blob storage is supported. This can be specified with `_destinationType=azure-block-blob` and `_destinationConnectionSettings` is the BASE64 encoding of the SAS token to the blob storage.

Example of the input:

`https://test-fhir-server/$export?_destinationType=azure-block-blob&_destinationConnectionSettings=QmxvYkVuZHBvaW50PWh0dHBzOi8vdGVzdC5ibG9iLmNvcmUud2luZG93cy5uZXQvO1F1ZXVlRW5kcG9pbnQ9aHR0cHM6Ly90ZXN0LnF1ZXVlLmNvcmUud2luZG93cy5uZXQvO0ZpbGVFbmRwb2ludD1odHRwczovL3Rlc3QuZmlsZS5jb3JlLndpbmRvd3MubmV0LztUYWJsZUVuZHBvaW50PWh0dHBzOi8vdGVzdC50YWJsZS5jb3JlLndpbmRvd3MubmV0LztTaGFyZWRBY2Nlc3NTaWduYXR1cmU9c3Y9MjAxOC0wMy0yOCZzcz1iJnNydD1zY28mc3A9cndkbGFjJnNlPTIwMTktMDYtMjlUMDg6NDE6MzNaJnN0PTIwMTktMDYtMDhUMDA6NDE6MzNaJnNwcj1odHRwcyZzaWc9SlhwMHdDdUM1eXJ6RmxLdmIlMkZvR0RaOW5IWDFOcEVkenNkclBHVUZmJTJCZnMlM0Q=`

## What is supported

Currently, only system-wide export is supported. Patient compartment is coming shortly. Checking the export status through the URL returned by the location header during the queuing is also supported. Cancelling the actual export job is currently not supported and will be supported soon.
