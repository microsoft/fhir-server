Still considered a work in progress, this tool evolved so that it provided output logging for both a POST to $import operation as well as GET for the $import status.
This import tool allows you to search for specific ndjson files from an azure container and then POST an $import operation to your configured FHIR endpoint to be processed.

This tool can perform the following operations.
1. It can monitor a long running $import job by polling the status. Simply provide the MonitorImportStatusEndpoint and the token fields in the config file. 
   Note that when this field is supplied then no other operation can be performed. Meaning you can't concurrently POST an $import. 
   Consequently when this setting is empty then the tool looks to POST an $import job.
2. Register an $import job by consuming ndjson blob files from the configured container and posting them to an OSS endpoint.
3. Register an $import job by consuming ndjson blob files from the configured container and posting them to a Paas endpoint.

If the tool stops for any reason then when you restart it will correctly skip any previously imported files by reading in the list of  
files that were already imported and saved to disk.

The app.config has descriptions for all of the necessary values that you will need to supply in order to run this tool.

When running FHIR OSS be sure to have these configuration settings in the portal:  
FhirServer__Operations__Import__Enabled = True
FhirServer__Operations__IntegrationDataStore__StorageAccountConnection = your_storageaccount_access_key_connection_string  
TaskHosting__MaxRunningTaskCount = some_value_greater_than_1

**You need to remove this setting to get import to work**
FhirServer__Operations__IntegrationDataStore__StorageAccountUri
