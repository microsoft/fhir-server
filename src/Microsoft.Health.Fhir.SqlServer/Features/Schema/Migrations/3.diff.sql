/*************************************************************
    Instance Schema
**************************************************************/
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

GO

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
CREATE PROCEDURE dbo.CreateInstanceSchema
    @name varchar(64),
    @currentVersion int,
    @maxVersion int,
    @minVersion int
AS
    SET NOCOUNT ON

    BEGIN

    DECLARE @timeout datetime2(0) = DATEADD(minute, 2, SYSUTCDATETIME())

    INSERT INTO dbo.InstanceSchema
        (Name, CurrentVersion, MaxVersion, MinVersion, Timeout)
    VALUES
        (@name, @currentVersion, @maxVersion, @minVersion, @timeout)

    END
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
--    @name
--         * The unique name for a particular instance
--     @currentVersion
--         * The current version of the schema that the given instance is using
--     @maxVersion
--         * The maximum supported schema version for the given instance
--     @minVersion
--         * The minimum supported schema version for the given instance
--
CREATE PROCEDURE dbo.UpsertInstanceSchema
    @name varchar(64),
    @currentVersion int,
    @maxVersion int,
    @minVersion int
    
AS
    SET NOCOUNT ON

    DECLARE @timeout datetime2(0) = DATEADD(minute, 2, SYSUTCDATETIME())

    IF EXISTS(SELECT *
        FROM dbo.InstanceSchema
        WHERE Name = @name)
    BEGIN
        UPDATE dbo.InstanceSchema
        SET CurrentVersion = @currentVersion, MaxVersion = @maxVersion, Timeout = @timeout
        WHERE Name = @name
    END
    ELSE
    BEGIN
        INSERT INTO dbo.InstanceSchema
            (Name, CurrentVersion, MaxVersion, MinVersion, Timeout)
        VALUES
            (@name, @currentVersion, @maxVersion, @minVersion, @timeout)
    END
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
