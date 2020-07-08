# Schema Migration Guide
This guide describes how to upgrade the schema for the FHIR server. The upgrade scripts are the T-SQL script that alters the database in some way and has a version encoded in its filename. The version will be an incrementing integer.



There are two ways to upgrade the schema
* Automatic schema upgrade
* Upgrade via schema migration tool

## How to toggle the upgrade option
There is a configurable setting which can be set to true/false based on the desired behavior.

* If it sets to 'true', then the schema would be upgraded automatically by the server itself.
* or, if it sets to 'false', then the schema would remain in the same state. The schema would only be upgraded by running the Schema migration tool

"SqlServer:SchemaOptions:AutomaticUpdatesEnabled": "true"

The setting should be considered for each web project
* R5
https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.R5.Web/Properties/launchSettings.json#L28
* R4
https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.R4.Web/Properties/launchSettings.json#L28
* Stu3
https://github.com/microsoft/fhir-server/blob/master/src/Microsoft.Health.Fhir.Stu3.Web/Properties/launchSettings.json#L27

## Automatic Schema Upgrade
Schema upgrade scripts are part of any upgrade package for the application and run automatically when the application starts.

### When might you choose this option
* Do not have access to DBA

  Since automatic upgrade does not need any permissions or role assigments and is performed at the server startup.

* Non Prod scenarios (dev/test)

  Since its  no harm that the dev/test application take some extra time during startup and any error/issue can be identified at the same time(if it happens). Also, it will reduce the overhead of role assignments and running the Schema migration tool.

## Upgrade via Schema migration tool
Schema upgrade scripts are part of any upgrade package for the application but the scripts will not run automatically on the application startup. In order to perform the schema upgrade, the admin/assigned roles should run the Schema migration tool.

### Schema migration tool
The Schema migration tool is the command line utility to perform schema upgrade on demand.
The tool needs to know which version the databases were at in order to select the appropriate migration scripts to upgrade the schema. 

#### Commands
The following commands are available via the tool

|Command|Description|Options|
|--------|---|---|
|current|Returns the current versions from the SchemaVersion table along with information on the instances using the given version|--server|
|available|Returns the versions greater than or equal to the current version along with links to the T-SQL scripts for upgrades|--server|
|apply|Applies the specified version(s) to the connection string supplied. Optionally can poll the FHIR server current version to apply multiple versions in sequence|--server,<br /> --connection-string,<br /> --next,<br /> --version,<br /> --latest,<br /> --force|

#### Options 

#### --next

It fetches the available versions and apply the next immediate available version to the current version.

#### -- version

It applies all the versions between current version and the specified version.

#### --latest

It fetches the available versions and apply all the versions between current and the latest available version.

#### --force
This option can be used with --next, --version and --latest. It skips all the checks to validate version and forces the tool to perform schema migration.


 #### Benefits
 * The database schema will evolve over time and a production server will need
 to be upgraded to the latest version of the code, which may required changes to the schema. For this reason, its not ideal to perform schema upgrade over startup as the schema upgrade can take longer often in proportion to the size of the database.
 * Allows you to setup proper application roles and permissions, e.g. Applications as only data reader/writers. Schema Admin etc.
 * The upgrade tool will not upgrade to the next version until all instances of the code are using the preceding version.
 * The tool is available as a .NET Core Global Tool

 ### General best practices for schema upgrade on Sql server
 * #### Back up the data before executing.
    
    In case something goes wrong during the implementation, you can’t afford to lose data. Make sure there are backup resources and that they’ve been tested before you proceed.
    
* #### Stick to the strategy.

    Too many data managers make a plan and then abandon it when the process goes “too” smoothly or when things get out of hand. The migration process can be complicated and even frustrating at times, so prepare for that reality and then 
    stick to the plan.

* #### Test, test, test

    Test before executing the migration and test after the migration is completed.


 ### Schema Versioning


 Note: this guide contains generalized advice and may not take your specific environment, application or environment into account.

