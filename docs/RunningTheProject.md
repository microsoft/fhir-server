# Running the Project

During development there are multiple ways to test the project. 

## Using Visual Studio

This project is primarily built in Visual Studio and has several files included to make it easier to test from within this environment. After cloning the repo the project can be run by selecting one of the Web projects and using Visual Studio's built in project runner. Once a Web project is selected the settings dropdown should auto populate with options for Default, Cosmos DB, or SQL Server database options.

## Using Terminal

To run this project from a terminal it will first need to be built. Once built an executable can be found in each of the Web project folders, for example in the R4.Web project: .\src\Microsoft.Health.Fhir.R4.Web\bin\Debug\net6.0\Microsoft.Health.Fhir.R4.Web.exe
Running this executable will start the project in the terminal. Note that it will use the appsettings options stored in the folder with the executable, which is only updated when the project is built. This file can also be manually edited if desired.

### Enabling extra logging

When running from Visual Studio additional logging is enabled by default. To enable this logging when running from a terminal change the logging level in appsettings.json as so:
"Logging": {
    "LogLevel": {
        "Default": "Information"
    }
}
