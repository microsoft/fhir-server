# BaseSchema

The following are the required schema for [Schema migration tool](SchemaMigrationTool.md). It include tables and stored procedures corresponding to these tables.

 ## 1. SchemaVersion
This table holds the information about the schema versions with their status.

- ### Create Table

        CREATE TABLE dbo.SchemaVersion
        (
            Version int PRIMARY KEY,
            Status varchar(10)
        )

- ### Create Stored Procedure SelectCurrentSchemaVersion 
    It returns the current version.

        CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
        AS
        BEGIN
            SET NOCOUNT ON

            SELECT MAX(Version)
            FROM SchemaVersion
            WHERE Status = 'completed'
        END
        GO

- ### Create Stored Procedure UpsertSchemaVersion
    It creates or updates a schema version entry.

        CREATE PROCEDURE dbo.UpsertSchemaVersion
            @version int,
            @status varchar(10)
        AS
            SET NOCOUNT ON

            IF EXISTS(SELECT *
                FROM dbo.SchemaVersion
                WHERE Version = @version)
            BEGIN
                UPDATE dbo.SchemaVersion
                SET Status = @status
                WHERE Version = @version
            END
            ELSE
            BEGIN
                INSERT INTO dbo.SchemaVersion
                    (Version, Status)
                VALUES
                    (@version, @status)
            END
        GO

## 2.  InstanceSchema
This table holds the information about the active server instances and their schema versions.

- ### Create Table

        CREATE TABLE dbo.InstanceSchema
        (
            Name varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
            CurrentVersion int NOT NULL,
            MaxVersion int NOT NULL,
            MinVersion int NOT NULL,
            Timeout datetime2(0) NOT NULL
        )

        CREATE UNIQUE CLUSTERED INDEX IXC_InstanceSchema ON dbo.InstanceSchema
        (
            Name
        )

        CREATE NONCLUSTERED INDEX IX_InstanceSchema_Timeout ON dbo.InstanceSchema
        (
            Timeout
        )

        GO

- ### Create Stored Procedure GetInstanceSchemaByName 
    It returns the instance schema record from the InstanceSchema table that has the matching name.    

        CREATE PROCEDURE dbo.GetInstanceSchemaByName
            @name varchar(64)
        AS
            SET NOCOUNT ON

            SELECT CurrentVersion, MaxVersion, MinVersion, Timeout
            FROM dbo.InstanceSchema
            WHERE Name = @name
        GO
        
- ### Create Stored Procedure UpsertInstanceSchema 
    It modifies an existing record in the InstanceSchema table.

        CREATE PROCEDURE dbo.UpsertInstanceSchema
            @name varchar(64),
            @maxVersion int,
            @minVersion int,
            @addMinutesOnTimeout int
            
        AS
            SET NOCOUNT ON

            DECLARE @timeout datetime2(0) = DATEADD(minute, @addMinutesOnTimeout, SYSUTCDATETIME())
            DECLARE @currentVersion int = (SELECT COALESCE(MAX(Version), 0)
                                        FROM dbo.SchemaVersion
                                        WHERE  Status = 'completed' OR Status = 'complete' AND Version <= @maxVersion)
            IF EXISTS(SELECT * FROM dbo.InstanceSchema
                    WHERE Name = @name)
            BEGIN
                UPDATE dbo.InstanceSchema
                SET CurrentVersion = @currentVersion, MaxVersion = @maxVersion, Timeout = @timeout
                WHERE Name = @name
                
                SELECT @currentVersion
            END
            ELSE
            BEGIN
                INSERT INTO dbo.InstanceSchema
                    (Name, CurrentVersion, MaxVersion, MinVersion, Timeout)
                VALUES
                    (@name, @currentVersion, @maxVersion, @minVersion, @timeout)

                SELECT @currentVersion
            END
        GO

- ### Create Stored Procedure DeleteInstanceSchema
    It delete all the expired records in the InstanceSchema table.       

        CREATE PROCEDURE dbo.DeleteInstanceSchema
            
        AS
            SET NOCOUNT ON

            DELETE FROM dbo.InstanceSchema
            WHERE Timeout < SYSUTCDATETIME()

        GO

- ### Create Stored Procedure SelectCompatibleSchemaVersions   
    It returns max and min compatible schema versions.    

        CREATE PROCEDURE dbo.SelectCompatibleSchemaVersions

        AS
        BEGIN
            SET NOCOUNT ON

            SELECT MAX(MinVersion), MIN(MaxVersion)
            FROM dbo.InstanceSchema
            WHERE Timeout > SYSUTCDATETIME()
        END
        GO
- ### Create Stored Procedure SelectCurrentVersionsInformation    
    It returns the current schema versions information.  

        CREATE PROCEDURE dbo.SelectCurrentVersionsInformation

        AS
        BEGIN
            SET NOCOUNT ON

            SELECT SV.Version, SV.Status, STRING_AGG(SCH.NAME, ',')
            FROM dbo.SchemaVersion AS SV LEFT OUTER JOIN dbo.InstanceSchema AS SCH
            ON SV.Version = SCH.CurrentVersion
            GROUP BY Version, Status

        END
        GO

