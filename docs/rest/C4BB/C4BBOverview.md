# CARIN IG for Blue Button®

This series of files can be used to configure your FHIR server to meet the requirements in the Carin IG for Blue Button (C4BB). A few notes about these files:

1. The C4BB.http file references files that are also stored in this folder. Make sure that you are pulling those from the same location. 
2. The sample resources file allows you to create interlinked resources for testing purposes. You need to create a resource and then add the ID for the created resource into the relevant variable. For example, when you create Patient1, you will grab that ID and store it earlier in the file where it says Patient1 = `<Patient1 ID>`. This will insure that you have links between the patients.
  
If you have feedback on these files to make them easier to use, please let us know.
