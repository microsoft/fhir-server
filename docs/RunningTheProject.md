# Running the Project

During development there are multiple ways to test the project. 

## Using Visual Studio

This project is primarily built in Visual Studio and has several files included to make it easier to test from within this environment. After cloning the repo the project can be run by selecting one of the Web projects and using Visual Studio's built in project runner. Once a Web project is selected the settings dropdown should auto populate with options for Default, Cosmos DB, or SQL Server database options.

### Running tests in Test Explorer

Test projects support both Microsoft Testing Platform (MTP), selected by `global.json` for `dotnet test`, and VSTest-based Visual Studio Test Explorer. Keep the `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` references in `Directory.Build.props` and their versions in `Directory.Packages.props`; MTP support alone does not provide a VSTest test host or adapter.

If discovery aborts with "Could not find testhost", verify these references are present, then build the solution so all test project outputs are updated before rerunning tests in Test Explorer.

## Using Terminal

To run this project from a terminal it will first need to be built. Once built an executable can be found in each of the Web project folders, for example in the R4.Web project: .\src\Microsoft.Health.Fhir.R4.Web\bin\Debug\net6.0\Microsoft.Health.Fhir.R4.Web.exe
Running this executable will start the project in the terminal. Note that it will use the appsettings options stored in the folder with the executable, which is only updated when the project is built. This file can also be manually edited if desired.

### Diagnosing canceled conditional deletes

When a conditional delete fails only because of cancellation, the server returns HTTP 408 with an `OperationOutcome` issue code of `timeout`, matching direct request cancellation. Only cancellation exceptions inside recognized delete partial-result wrappers and nonempty aggregates qualify; mixed storage failures, unknown exceptions, and empty aggregates remain HTTP 500.

Cancellation does not roll back completed deletions. The `Items-Deleted` response header still reports completed deletions, and executed audit records identify the affected resources. A disconnected client might not receive the response.

This classification does not identify the cancellation source, change timeouts or retries, or make the delete complete. Upstream delete error logs and the exception-bearing cancellation warning remain; log-derived exception metrics and alerts can therefore still fire. An HTTP 408 response alone is not evidence that exception-metric emission has stopped.

### Enabling extra logging

When running from Visual Studio additional logging is enabled by default. To enable this logging when running from a terminal change the logging level in appsettings.json as so:
"Logging": {
    "LogLevel": {
        "Default": "Information"
    }
}
