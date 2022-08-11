# FHIR Schema Manager

### What is it?
Schema Manager is a command line app that upgrades the schema in your database from one version to the next through migration scripts.

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
- This table holds all schema versions that have been applied to the database.

**InstanceSchema**
- Each FHIR instance reports the schema version it is at, as well as the versions it is compatible with, to the InstanceSchema database table.

------------

### Terminology

**Current database version**
- The maximum SchemaVersion version in the database.

**Current instance version**
- The maximum SchemaVersion version in the database that falls at or below the SchemaVersionConstants.Max value. For example, if the current database version is 25, but SchemaVersionConstants.Max is 23, the instance's current version will be 23.

**Available version**
- Any version greater than the current database version.

**Compatible version**
- Any version from SchemaVersionConstants.Min to SchemaVersionConstants.Max (inclusive).

------------

### How does it work?

Schema Manager runs through the following steps:
1. Verifies all arguments are supplied and valid.
2. Calls the [healthcare-shared-components ApplySchema function](https://github.com/microsoft/healthcare-shared-components/blob/main/src/Microsoft.Health.SqlServer/Features/Schema/Manager/SqlSchemaManager.cs#L53), which:
	1. Ensures the base schema exists.
	2. Ensures instance schema records exist.
		1. Since FHIR Server implements its own ISchemaClient (FhirSchemaClient), if there are no instance schema records, the upgrade continues uninterrupted. In healthcare-shared-components, this would throw an exception and cancel the upgrade.
	3. Gets all available versions and compares them against all compatible versions.
	4. Based on the current database schema version:
		1. If there is no version (base schema only), the latest full migration script is applied.
		2. If the current version is >= 1, each available version is applied one at a time until the database's schema version reaches the desired version input by the user (latest, next, or a specific version).

------------

### Caveats

Schema Manager works under the assumption that it will be updated at the same time as any FHIR binaries. It's possible to end up with a database in a bad state when running Schema Manager with a different tag version than the FHIR binary. For example, you could have a database upgraded to schema version 25, but the binary only supports up to schema version 23.

Schema Manager is programmed to upgrade the database of an existing, running FHIR instance, or against a new database. If SchemaManager is run against an existing database with no running instances, SchemaManager will apply the latest SchemaVersion possible, and not take into account the compatibility from running instances. This is because the InstanceSchema table is only populated when FHIR services are running.

------------

### SQL Script Locations

- [Base Schema Script](https://github.com/microsoft/healthcare-shared-components/blob/main/src/Microsoft.Health.SqlServer/Features/Schema/Migrations/BaseSchema.sql)

- [FHIR Migration Scripts](https://github.com/microsoft/fhir-server/tree/main/src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Migrations)
