# Running the Project

During development there are multiple ways to test the project. 

## Install the SDKs

Install the .NET 11 SDK pinned in `global.json` and the .NET 10 SDK pinned in `build/dotnet10-compat/global.json`. The .NET 10 runtime is required by the SQL build generators and .NET 10 tests. CI and the dev container install both SDKs.

NuGet packages target `net11.0` and `net10.0`. Web archives, integration test archives, and Docker images use only `net11.0`, with the existing artifact names and image tags.

## Using Visual Studio

This project is primarily built in Visual Studio and has several files included to make it easier to test from within this environment. After cloning the repo the project can be run by selecting one of the Web projects and using Visual Studio's built in project runner. Once a Web project is selected the settings dropdown should auto populate with options for Default, Cosmos DB, or SQL Server database options.

### Running tests in Test Explorer

Test projects support both Microsoft Testing Platform (MTP), selected by `global.json` for `dotnet test`, and VSTest-based Visual Studio Test Explorer. Keep the `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` references in `Directory.Build.targets` and their versions in `Directory.Packages.props`; MTP support alone does not provide a VSTest test host or adapter.

If discovery aborts with "Could not find testhost", verify these references are present, then build the solution so all test project outputs are updated before rerunning tests in Test Explorer.

## Using Terminal

Build the project before running it from a terminal. The .NET 11 R4 web executable is `.\src\Microsoft.Health.Fhir.R4.Web\bin\Debug\net11.0\Microsoft.Health.Fhir.R4.Web.exe`.
Running this executable will start the project in the terminal. Note that it will use the appsettings options stored in the folder with the executable, which is only updated when the project is built. This file can also be manually edited if desired.

Run unit tests for each framework with `dotnet test --project <unit-test.csproj> -f net11.0` and `dotnet test --project <unit-test.csproj> -f net10.0`.

### Enabling extra logging

When running from Visual Studio additional logging is enabled by default. To enable this logging when running from a terminal change the logging level in appsettings.json as so:
"Logging": {
    "LogLevel": {
        "Default": "Information"
    }
}
