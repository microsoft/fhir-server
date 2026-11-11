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
Partition IDs cannot match a FHIR resource type or the service routes `metadata`,
`_history`, or `CustomError` (case-insensitive). These conflicts fail startup
instead of changing the meaning of unprefixed requests. Cosmos DB storage
rejects nonempty partition configuration until its persistence supports partition isolation.

Requests identify a partition through the first path segment, for example
`/partition-a/Patient`. The prefix becomes part of the FHIR base URL, and the
request context's `PartitionId` is preserved in clones and batch/transaction entries.
When there is exactly one partition, FHIR requests without a partition prefix use that partition.
When there are multiple partitions, requests without a recognized prefix return 404,
except for the unprefixed `/health/check` and service-wide authentication endpoints.
Authentication endpoints do not select a partition, even when only one partition is configured:
`/connect/token`, `/connect/introspect`, `/.well-known/openid-configuration`,
`/.well-known/oauth-authorization-server`, `/.well-known/jwks`,
`/.well-known/smart-configuration`, `/AadSmartOnFhirProxy/authorize`,
`/AadSmartOnFhirProxy/token`, and `/AadSmartOnFhirProxy/callback/{encodedRedirect}`.
Their existing authentication, validation, and feature-enable requirements still apply.
With no configured partitions, existing single-service behavior is unchanged.

For SQL storage, generated search queries resolve the request's `PartitionId`
through the partition manager and qualify physical tables as
`[DatabaseName].[dbo].[TableName]`. This includes search indexes, lookup
subqueries, chains, and includes. Database identifiers are escaped, and query
hashes include the database qualification. Requests without a partition retain the
existing two-part table names. With no configured partitions, SQL always uses the
connection database and legacy object names, regardless of the request context's
partition ID. When partitions are configured, an unknown partition ID cannot fall back
to the connection database during query generation.

Stored-procedure calls also resolve the request partition and use
`[DatabaseName].[dbo].[ProcedureName]`. This covers reads, writes, search
shortcuts and custom-query procedures, queues, change feed, and maintenance
calls made with a partition context. The shared retry path and direct execution
paths preserve parameters, timeouts, and transaction handling. Names are safely
quoted, and repeated command preparation does not add another database prefix.
An unknown partition is rejected before a stored-procedure call can execute.
Calls with no partition context, including startup and background operations,
retain their original database behavior; this does not add partition iteration or
persist partition identity in background jobs.
When a dedicated `EventLogConnectionString` is configured in the SQL Parameters
table, event logging uses that connection's database and `dbo.LogEvent`, without
partition qualification.

The service continues to use one connection string. All partition databases must
be in the same elastic pool, with SQL support and permissions for cross-database
three-part names. This capability has not been validated against a live elastic
pool as part of this implementation.

**This is not complete partition isolation.** Database-specific metadata caches,
partition-specific authorization, and background-job partitioning remain unimplemented.
Handwritten SQL text is not automatically rewritten to select a partition
database, and database schema/procedure bodies are unchanged.
Do not rely on these settings to isolate partition data.
