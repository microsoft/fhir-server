# Da Vinci Payer Data Exchange IG

This series of files can be used to configure your FHIR server to meet the requirements in the [Da Vinci Payer Data Exchange IG](https://hl7.org/fhir/us/davinci-pdex/index.html) (PDex). A few notes about these files:

The USCore.http file references files that are also stored in this folder. Make sure that you are pulling those from the same location
The USCore.http file has all of the profiles for US Core which are needed to pass without one of the capability statement tests in Touchstone without warnings.
The membermatch.http file has the data that needs to be loaded to pass the member-match test in Touchstone. 
The PDex_Sample_Data.http file allows you to create interlinked resources for testing purposes. Create the resources in order to get the links to work.

If you have feedback on these files to make them easier to use, please let us know.
