/*************************************************************
    Instance Schema
**************************************************************/
CREATE TABLE dbo.InstanceSchema
(
    Name varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CurrentVersion int NOT NULL,
    MaxVersion int NOT NULL,
    MinVersion int NOT NULL,
    Timeout datetime2(7) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_InstanceSchema ON dbo.InstanceSchema
(
    Name
)

/*************************************************************
    Stored procedures for InstanceSchema
**************************************************************/
--
-- STORED PROCEDURE
--     CreateInstanceSchema.
--
-- DESCRIPTION
--     Creates a new row to the InstanceSchema table.
--
-- PARAMETERS
--     @name
--         * The unique name for a particular instance
--     @currentVersion
--         * The current version of the schema that the given instance is using
--     @maxVersion
--         * The maximum supported schema version for the given instance
--     @minVersion
--         * The minimum supported schema version for the given instance
--
-- RETURN VALUE
--     The row version of the created instance record.
--
CREATE PROCEDURE dbo.CreateInstanceSchema
    @name varchar(64),
    @currentVersion int,
    @maxVersion int,
    @minVersion int
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @timeout datetime2(7) = DATEADD(minute, 2, SYSUTCDATETIME())

    INSERT INTO dbo.InstanceSchema
        (Name, CurrentVersion, MaxVersion, MinVersion, Timeout)
    VALUES
        (@name, @currentVersion, @maxVersion, @minVersion, @timeout)
  
    SELECT CAST(MIN_ACTIVE_ROWVERSION() AS INT)

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Gets schema information given its instance name.
--
-- DESCRIPTION
--     Retrieves the instance schema record from the InstanceSchema table that has the matching name.
--
-- PARAMETERS
--     @name
--         * The unique name for a particular instance
--
-- RETURN VALUE
--     The matching record.
--
CREATE PROCEDURE dbo.GetInstanceSchemaByName
    @name varchar(64)
AS
    SET NOCOUNT ON

    SELECT CurrentVersion, MaxVersion, MinVersion, Timeout
    FROM dbo.InstanceSchema
    WHERE Name = @name
GO

--
-- STORED PROCEDURE
--     Updates an instance schema.
--
-- DESCRIPTION
--     Modifies an existing record in the InstanceSchema table.
--
-- PARAMETERS
--     @name
--         * The unique name for a particular instance
--     @maxVersion
--         * The maxium supported schema version for the given instance
--
-- RETURN VALUE
--     The row version of the updated instance schema.
--
CREATE PROCEDURE dbo.UpdateInstanceSchema
    @name varchar(64),
    @maxVersion int
    
AS
    SET NOCOUNT ON

    IF EXISTS(SELECT *
        FROM dbo.InstanceSchema
        WHERE Name = @name)
    BEGIN
        DECLARE @timeout datetime2(7) = DATEADD(minute, 2, SYSUTCDATETIME())

        UPDATE dbo.InstanceSchema
        SET MaxVersion = @maxVersion, Timeout = @timeout
        WHERE Name = @name
    END
    ELSE
    BEGIN
        THROW 50404, 'Instance record not found', 1;
    END
  
    SELECT MIN_ACTIVE_ROWVERSION()

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Deletes an instance schema.
--
-- DESCRIPTION
--     Deletes an existing record in the InstanceSchema table.
--
-- PARAMETERS
--     @name
--         * The unique name for a particular instance
--
CREATE PROCEDURE dbo.DeleteInstanceSchemaByName
    @name varchar(64)
    
AS
    SET NOCOUNT ON

    DELETE FROM dbo.InstanceSchema
    WHERE Name = @name

GO
