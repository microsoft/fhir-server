# Running the Project

During development there are multiple ways to test the project. 

## Using Visual Studio

This project is primarily built in Visual Studio and has several files included to make it easier to test from within this environment. After cloning the repo the project can be run by selecting one of the Web projects and using Visual Studio's built in project runner. Once a Web project is selected the settings dropdown should auto populate with options for Default, Cosmos DB, or SQL Server database options.

### Running tests in Test Explorer

Test projects support both Microsoft Testing Platform (MTP), selected by `global.json` for `dotnet test`, and VSTest-based Visual Studio Test Explorer. Keep the `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` references in `Directory.Build.targets` and their versions in `Directory.Packages.props`; MTP support alone does not provide a VSTest test host or adapter.

If discovery aborts with "Could not find testhost", verify these references are present, then build the solution so all test project outputs are updated before rerunning tests in Test Explorer.

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

### Configuring partitions

Partitions can be configured at startup in `appsettings.json`:

```json
{
  "FhirServer": {
    "PartitionConfiguration": {
      "Partitions": [
        { "Id": "partition-a", "DatabaseName": "database-a" },
        { "Id": "partition-b", "DatabaseName": "database-b" }
      ]
    }
  }
}
```

Partition IDs are case-sensitive, unique URL segments containing only ASCII letters,
digits, `-`, `_`, `.`, or `~`; `.` and `..` are not valid IDs. Each partition must
have a nonblank database name. Invalid settings prevent startup. The partition
manager retains a read-only startup snapshot; changing partitions requires a restart.
Cosmos DB storage rejects nonempty partition configuration.

Requests identify a partition with `/partition/<id>`, for example
`/partition/partition-a/Patient`.
When there is exactly one partition, FHIR requests without a partition prefix use that partition.
When there are multiple partitions, requests without a recognized prefix return 404,
except for the unprefixed `/health/check` and service-wide authentication endpoints.
An explicit `/partition` prefix with a missing or unknown ID returns 404, even
when exactly one partition is configured.
With no configured partitions, existing single-service behavior is unchanged.

The service continues to use one connection string. All partition databases must
be in the same elastic pool, with SQL support and permissions for cross-database
three-part names.
