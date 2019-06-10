# Bulk Export

This feature allows data from the FHIR server to be exported. More detail can be found in the [spec](https://github.com/HL7/bulk-data/blob/master/spec/export/index.md).

The feature is currently under preview and is turned off by default. To enable the feature, update the `FhirServer:Operations:Export:Enabled` setting to be true.

## Specifying destination

One customization that we have done in addition to the spec is _requiring_ the destination information to be supplied. This is a breaking change from the spec. We are planning to support default destination to maintain compatibility with spec.

The destination type can be specified by using `_destinationType` query parameter and any destination specific settings can be specified as base64 encoded string by using `_destinationConnectionSettings` query parameter.

Currently, we only support Azure Blob storage as the destination. This can be specified with `_destinationType=azure-block-blob` and `_destinationConnectionSettings=[base64 encoding of connection string]`.

We recommend to use supply connection string with SAS token so that the token can expire after certain period of time.

Example of connection string with SAS token:

```
BlobEndpoint=https://test.blob.core.windows.net/;QueueEndpoint=https://test.queue.core.windows.net/;FileEndpoint=https://test.file.core.windows.net/;TableEndpoint=https://test.table.core.windows.net/;SharedAccessSignature=sv=2018-03-28&ss=b&srt=sco&sp=rwdlac&se=2019-06-29T08:41:33Z&st=2019-06-08T00:41:33Z&spr=https&sig=JXp0wDoD5yrzFlKvb%2FoEOF9nHX1NpEdzsdrPGUFf%2Bfs%3D
```

Base64 encoding of the connection string with SAS token:

```
QmxvYkVuZHBvaW50PWh0dHBzOi8vZmhpcmJ1bGtpbXBvcnR0ZXN0LmJsb2IuY29yZS53aW5kb3dzLm5ldC87UXVldWVFbmRwb2ludD1odHRwczovL2ZoaXJidWxraW1wb3J0dGVzdC5xdWV1ZS5jb3JlLndpbmRvd3MubmV0LztGaWxlRW5kcG9pbnQ9aHR0cHM6Ly9maGlyYnVsa2ltcG9ydHRlc3QuZmlsZS5jb3JlLndpbmRvd3MubmV0LztUYWJsZUVuZHBvaW50PWh0dHBzOi8vZmhpcmJ1bGtpbXBvcnR0ZXN0LnRhYmxlLmNvcmUud2luZG93cy5uZXQvO1NoYXJlZEFjY2Vzc1NpZ25hdHVyZT1zdj0yMDE4LTAzLTI4JnNzPWImc3J0PXNjbyZzcD1yd2RsYWMmc2U9MjAxOS0wNi0yOVQwODo0MTozM1omc3Q9MjAxOS0wNi0wOFQwMDo0MTozM1omc3ByPWh0dHBzJnNpZz1KWHAwd0N1QzV5cnpGbEt2YiUyRm9EVlo5bkhYMU5wRWR6c2RyUEdVRmYlMkJmcyUzRA==
```

Example of the URL used to queue a new job using the destination:

```
https://test-fhir-server/$export?_destinationType=azure-block-blob&_destinationConnectionSettings=QmxvYkVuZHBvaW50PWh0dHBzOi8vdGVzdC5ibG9iLmNvcmUud2luZG93cy5uZXQvO1F1ZXVlRW5kcG9pbnQ9aHR0cHM6Ly90ZXN0LnF1ZXVlLmNvcmUud2luZG93cy5uZXQvO0ZpbGVFbmRwb2ludD1odHRwczovL3Rlc3QuZmlsZS5jb3JlLndpbmRvd3MubmV0LztUYWJsZUVuZHBvaW50PWh0dHBzOi8vdGVzdC50YWJsZS5jb3JlLndpbmRvd3MubmV0LztTaGFyZWRBY2Nlc3NTaWduYXR1cmU9c3Y9MjAxOC0wMy0yOCZzcz1iJnNydD1zY28mc3A9cndkbGFjJnNlPTIwMTktMDYtMjlUMDg6NDE6MzNaJnN0PTIwMTktMDYtMDhUMDA6NDE6MzNaJnNwcj1odHRwcyZzaWc9SlhwMHdDdUM1eXJ6RmxLdmIlMkZvR0RaOW5IWDFOcEVkenNkclBHVUZmJTJCZnMlM0Q=
```

## What is supported

Currently, only system-wide export is supported. Patient compartment is coming shortly. Checking the export status through the URL returned by the location header during the queuing is also supported. Cancelling the actual export job is currently not supported and will be supported soon.
