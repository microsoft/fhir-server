#Async Operation Generator
This tool creates and updates files needed to make a new async operation. The files generated are templates, they still need to be filled in. The code will not compile after generation.
The tool needs to be called from the root folder for the fhir-server repo.
Example for a operation called "Bulk Delete":
PS D:\repos\fhir-server> .\tools\AsyncJobGenerator\GenerateAsyncJob -OperationName "Bulk Delete" -System -ResourceType

##Known Limitations
- The file src\Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.projitems still needs to be manually updated to include the new controller or the file won't compile or display in VS.
- The Controller will include methods for all three operation levels regardless of the switches given to the script
- Does not create the operation documents or update the operation definition controller

##Future Enhancements
- Fill in JobType and QueueType numbers automatically
- Fill in Get and Cancel flows automatically. They are standardized accross operations.
