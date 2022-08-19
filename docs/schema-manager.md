# FHIR Schema Manager for SQL

### What is it?
Schema Manager is a command line app that upgrades the schema in SQL database from one version to the next through migration scripts.

------------

### How do you use it?
FHIR Schema Manager currently has one command (**apply**) with the following options:

| Option | Description |
| ------------ | ------------ |
| `-cs, --connection-string` | The connection string of the SQL server to apply the schema update. (REQUIRED) |
| `-mici, --managed-identity-client-id` | The client ID of the managed identity to be used. |
| `-at, --authentication-type` | The authentication type to use. Valid values are `ManagedIdentity` and `ConnectionString`. |
| `-v, --version` | Applies all available versions from the current database version to the specified version. |
| `-n, --next` | Applies the next available database version. |
| `-l, --latest` | Applies all available versions from the current database version to the latest. |
| `-f, --force` | The schema migration is run without validating the specified version. |
| `-?, -h, --help` | Show help and usage information. |

You can view the most up-to-date options by running the following command:
`.\Microsoft.Health.Fhir.SchemaManager.Console.exe apply -?`

Example command line usage:
`.\Microsoft.Health.Fhir.SchemaManager.Console.exe apply --connection-string "server=(local);Initial Catalog=Fhir;TrustServerCertificate=True;Integrated Security=True" --version 20`

`.\Microsoft.Health.Fhir.SchemaManager.Console.exe apply -cs "server=(local);Initial Catalog=Fhir;TrustServerCertificate=True;Integrated Security=True" --latest`

------------

### Important Database Tables

**SchemaVersion**
- This table holds all schema versions that have been applied to the database. This table holds the value of (Version, Status). Version indicates the SchemaVersion that is applied to the database. Status indicates what happened during the specific schema version migration. If the migration failed then there will be an entry with (x, failed). If the migration is succesful then there will be an entry with (x, completed)

**InstanceSchema**
- Each FHIR instance reports the schema version it is at, as well as the versions it is compatible with, to the InstanceSchema database table. This table holds the value of (Name, CurrentVersion, MaxVersion, MinVersion, Timeout). 
Name - Instance name specified as a guid
CurrentVersion - The schema version on which the specific fhir instance is on
MaxVersion - SchemaVersionConstant.Max
MinVersoin - SchemaVersionConstant.Min
Timeout - 

------------

### Terminology

**Current schema version**
- The highest schema version applied to the SQL Database at present. This value is stored in SchemaVersion table.

**Current instance schema version**
- The highest schema version applied to the SQL Database at present that falls at or below the SchemaVersionConstants.Max value.

**Available schema version**
- Any schema version greater than the current schema version.

**Compatible schema version**
- Any schema version from SchemaVersionConstants.Min to SchemaVersionConstants.Max (inclusive).

------------

### How does it work?

Schema Manager runs through the following steps:
1. Verifies all arguments are supplied and valid.
2. Calls the [healthcare-shared-components ApplySchema function](https://github.com/microsoft/healthcare-shared-components/blob/main/src/Microsoft.Health.SqlServer/Features/Schema/Manager/SqlSchemaManager.cs#L53), which:
	1. Ensures the base schema exists.
	2. Ensures instance schema records exist.
		1. Since FHIR Server implements its own ISchemaClient (FhirSchemaClient), if there are no instance schema records, the upgrade continues uninterrupted. In healthcare-shared-components, this would throw an exception and cancel the upgrade.
	3. Gets all available versions and compares them against all compatible schema versions.
	4. Based on the current database schema version:
		1. If there is no schema version (base schema only), the latest full migration script is applied.
		2. If the current schema version is >= 1, each available achema version is applied one at a time until the database's schema version reaches the desired schema version input by the user (latest, next, or a specific schema version).

------------

### Caveats

1. Schema Manager is deployed in a separate container than the FHIR service and it works under the assumption that it will be updated at the same time as any FHIR binaries. It's possible to end up with a database in a bad state when running Schema Manager with a different tag version than the FHIR binary. For example, Schema manager could have the latest schema version 25 which will upgrad the database to schema version 25, but the FHIR service could still be on older binary that only supports up to schema version 23. One way to avoid this is to ensure binaries for Schema manager and FHIR service are updated at the same time.

2. Schema Manager is programmed to upgrade the database of an existing, running FHIR instance, or against a new database. 
	1. If SchemaManager is run against an existing database with no running instances, SchemaManager will apply the latest SchemaVersion possible, and not take into account the compatibility from running instances. This is because the InstanceSchema table is only populated when FHIR services are running.
	2. If SchemaManager is run against an existing database with previously running instances (assuming its not running right now) which will have the InstanceSchema table populated, SchemaManager will apply the (Current Schema Version + 1) SchemaVersion, and will fail when try to apply the next version (Current Schema Version + 2).

------------

### SQL Script Locations

- [Base Schema Script](https://github.com/microsoft/healthcare-shared-components/blob/main/src/Microsoft.Health.SqlServer/Features/Schema/Migrations/BaseSchema.sql)

- [FHIR Migration Scripts](https://github.com/microsoft/fhir-server/tree/main/src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations)
