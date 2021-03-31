# SQL Server schema Migration Tool
The SQL Server schema migration tool is the command line utility to perform SQL Server schema upgrade on demand. The tool will automatically identify the current version of the database in order to apply appropriate migration scripts.

Note - The tool can't downgrade a schema version.

- #### Prerequisites
    The FHIR Server for Azure has the 
    - minimum supported schema version >= 4 and 
    - the current schema version is null or >= 3.

    Note: If the FHIR Server for Azure is running on current schema version < 3(i.e. 1 or 2), manual intervention is required to upgrade the current schema version to 3.

    #### Manual steps to upgrade current schema version(1 or 2) to 3 using any SQL Editor
    1. If the current schema version is 1, then
        1. Execute the content of 2.diff.sql.
        2. After the previous step has run successfully, execute the query 'INSERT INTO dbo.SchemaVersion VALUES (2, 'completed')'
        3. Execute the content of 3.diff.sql.
        4. After the previous step has run successfully, execute the query 'INSERT INTO dbo.SchemaVersion VALUES (3, 'completed')'

    2. If the current schema version is 2, then
        1. Execute the content of 3.diff.sql.
        2. After the previous step has run successfully, execute the query 'INSERT INTO dbo.SchemaVersion VALUES (3, 'completed')'

- #### How to install the tool

     - ##### Install the tool from public feed

        It can be installed using below steps:
            
        - Copy [nuget.config](https://github.com/microsoft/fhir-server/blob/main/nuget.config) in the current folder or any desired folder
        -  Open a terminal/command prompt and hit command

                dotnet tool install -g Microsoft.Health.SchemaManager

- #### How to uninstall the tool
    Open a terminal/command prompt and hit command

        dotnet tool uninstall -g Microsoft.Health.SchemaManager          

- #### Commands
    The tool supports following commands:

    |Command|Description|Options|Usage
    |--------|---|---|---|
    |current|Returns the current versions from the SchemaVersion table along with information on the instances using the given version|--server/-s|schema-manager current [options]
    |available|Returns the versions greater than or equal to the current version along with the path to the T-SQL scripts for upgrades|--server/-s|schema-manager available [options]
    |apply|Applies the specified version(s) to the connection string supplied. Optionally can poll the FHIR server current version to apply multiple versions in sequence|--server/-s,<br /> --connection-string/-cs,<br /> --next/-n,<br /> --version/-v,<br /> --latest/-l,<br /> --force/-f|schema-manager apply [options]

- #### Options 

    |Option|Description|Usage
    |--------|---|---|
    |--server/-s|To provide the host url of the application| --server https://localhost:12345|
    --connection-string/-cs| To provide the connection string  of the sql database| --connection-string "server=(local);Initial Catalog=DATABASE_NAME;Integrated Security=true"|
    --next/-n| It fetches the available versions and apply the next immediate available version to the current version| ---next|
    --version/-v|To provide the schema version to upgrade. It applies all the versions between current version and the specified version|--version 5|
    --latest/-l|It fetches the available versions and apply all the versions between current and the latest available version|--latest|
    --force/-f|This option can be used with --next, --version and --latest. It skips all the checks to validate version and forces the tool to perform schema migration|--force
