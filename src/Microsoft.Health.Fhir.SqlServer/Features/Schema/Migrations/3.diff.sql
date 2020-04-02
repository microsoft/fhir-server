
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

    SELECT @name

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
--     Update an instance schema.
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

        SELECT @name
    END
    ELSE
    BEGIN
        INSERT INTO dbo.InstanceSchema
            (Name, CurrentVersion, MaxVersion, MinVersion, Timeout)
        VALUES
            (@name, @currentVersion, @maxVersion, @minVersion, @timeout)

        SELECT @name
    END

GO

--
-- STORED PROCEDURE
--     Delete instance schema information.
--
-- DESCRIPTION
--     Delete all the expired records in the InstanceSchema table.
--
CREATE PROCEDURE dbo.DeleteInstanceSchema
    
AS
    SET NOCOUNT ON

    DELETE FROM dbo.InstanceSchema
    WHERE Timeout < SYSUTCDATETIME()

GO

--
--  STORED PROCEDURE
--      SelectCompatibleSchemaVersions
--
--  DESCRIPTION
--      Selects the compatible schema versions
--      The returning max compatible version is not greater than the most recent, complete schema version
--
--  RETURNS
--      The maximum and minimum compatible versions
--
CREATE PROCEDURE dbo.SelectCompatibleSchemaVersions

AS
BEGIN
    SET NOCOUNT ON

    DECLARE @maxSchemaVersion int,
            @minSchemaVersion int

    Select @maxSchemaVersion = MIN(MaxVersion), @minSchemaVersion = MAX(MinVersion)
    FROM dbo.InstanceSchema
    WHERE Timeout > SYSUTCDATETIME()

    SELECT @minSchemaVersion, MAX(Version)
    FROM dbo.SchemaVersion
    WHERE Status = 'complete' AND Version <= @maxSchemaVersion

END
GO

--
--  STORED PROCEDURE
--      SelectCurrentVersionsInformation
--
--  DESCRIPTION
--      Selects the current schema versions information
--
--  RETURNS
--      The current versions, status and server names using that version
--
CREATE PROCEDURE dbo.SelectCurrentVersionsInformation

AS
BEGIN
    SET NOCOUNT ON

    SELECT SV.Version, SV.Status, STRING_AGG(SCH.NAME, ',')
    FROM
    dbo.SchemaVersion AS SV LEFT OUTER JOIN dbo.InstanceSchema as SCH
    ON SV.Version = SCH.CurrentVersion
    GROUP BY Version, Status

END
GO

