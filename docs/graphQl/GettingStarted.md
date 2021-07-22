# GraphQl endpoint for FHIR-Server
> A GraphQL prototype for the [HL7 FHIR specification](https://www.hl7.org/fhir/) based on the current implementation guide 
> for [GraphQL with FHIR](https://build.fhir.org/graphql.html).
 
## What Can I do with GraphQL in FHIR-Server?
* ✅ Get first 10 Patients in your FHIR-Server with specified properties based on [Patient Resource](https://www.hl7.org/fhir/patient.html).
* ✅ Get Patient by ID with specified properties.
* ✅ Get more than one Patient by ID with specified properties.

![Patients-with-more-attributes](https://user-images.githubusercontent.com/33185677/125542267-e68818d0-eefb-422e-91d4-177c23a89f64.gif)

## Building locally

### Getting started
* Follow one-time steps in [Build and debug locally](https://github.com/microsoft/fhir-server/wiki/Build-and-debug-locally).
* Change branch to feature/graphql-prototype: `git checkout feature/graphql-prototype`
* Install .Net core SDK version as listed in the global.json file (fhir-server/global.json)
https://dotnet.microsoft.com/download/dotnet-core/

### Running locally
* Ensure the Cosmos DB Emulator and Azure Storage Emulator are running.
* Open Microsoft.Health.Fhir.sln, set Microsoft.Health.Fhir.R4.Web as StartUp Project, and press F5.
* If you would like to debug locally without the need to fetch an auth token, you can disable the "FhirServer:Security:Enabled" 
  setting in the "appSettings.json" file.
* Ensure FHIR REST API is working, check if you are able to hit the metadata endpoint (https://localhost:44348/metadata).
* Ensure GraphQL endpoint is working, check if you are able to hit (https://localhost:44348/graphql). You should be able to
  see the [Banana Cake Pop UI](https://chillicream.com/docs/bananacakepop).

## Manual Testing
* [Running manual test scenarios with Banana Cake Pop UI](https://github.com/microsoft/fhir-server/blob/feature/graphql-prototype/docs/graphQl/TestingWithBananaCakePop.md).
* Using Visual Studio Code's REST Client to run manual test scenarios.
    * Navigate to the `docs\graphQl\TestingVSCodeExtension` folder, open the `.http` test file you would like to use in Visual Studio Code
    * Click "Send Request" above each HTTP request in the file.
    * To learn about VS Code extension see [How to use Visual Studio Code's REST Client](https://github.com/microsoft/fhir-server/blob/main/docs/rest/HowToUseVSCodeRestClient.md).

## Next Steps and Important Notes
* Currently it is not supporting pagination, when you are doing a **patients** query, you are getting the first 10 patients in the server.
* FHIR specification says that the endpoint should be `[base]\$graphql`, currently it is `[base]\graphql`.
* There are some fields from the schema that are not supported such as `patient -> deceasedBoolean`. If you want to see the current schema, you can find the .graphql file in `src/Microsoft.Health.Fhir.Core/Data/GraphQL/patient.graphql` or `src/Microsoft.Health.Fhir.Core/Data/GraphQL/types.graphql` for further information.
