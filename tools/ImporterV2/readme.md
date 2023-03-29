Still considered a work in progress, this tool evolved so that it provided output logging for both a POST to $import operation as well as GET for the $import status.
This import tool allows you to search for specific ndjson files from an azure container and then POST an $import operation to your configured FHIR endpoint to be processed.

This tool can perform the following operations.
1. It can monitor a long running $import job by polling the status. Simply provide the MonitorImportStatusEndpoint and the token fields in the config file. 
   Note that when this field is supplied then no other operation can be performed. Meaning you can't concurrently POST an $import. 
   Consequently when this setting is empty then the tool looks to POST an $import job.
2. Run an $import job by consuming ndjson blob files from the configured container and posting them to an OSS endpoint.
3. Run an $import job by consuming ndjson blob files from the configured container and posting them to a Paas endpoint.

The app.config has descriptions for all of the necessary values that you will need to supply in order to run this tool.
