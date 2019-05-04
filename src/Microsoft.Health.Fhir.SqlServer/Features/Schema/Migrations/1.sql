-- NOTE: This script DROPS AND RECREATES all database objects.
-- Style guide: please see: https://github.com/ktaranov/sqlserver-kit/blob/master/SQL%20Server%20Name%20Convention%20and%20T-SQL%20Programming%20Style.md


/*************************************************************
    Drop existing objects
**************************************************************/

DECLARE @sql nvarchar(max) =''

SELECT @sql = @sql + 'DROP PROCEDURE ' + name + '; '
FROM sys.procedures

SELECT @sql = @sql + 'DROP TABLE ' + name + '; '
FROM sys.tables

SELECT @sql = @sql + 'DROP TYPE ' + name + '; '
FROM sys.table_types

SELECT @sql = @sql + 'DROP SEQUENCE ' + name + '; '
FROM sys.sequences

EXEC(@sql)

GO

/*************************************************************
    Configure database
**************************************************************/

-- Enable RCSI
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON

-- Avoid blocking queries when statistics need to be rebuilt
ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS_ASYNC ON
GO

/*************************************************************
    Schema bootstrap
**************************************************************/

CREATE TABLE dbo.SchemaVersion
(
    Version int PRIMARY KEY,
    Status varchar(10)
)

INSERT INTO dbo.SchemaVersion
VALUES
    (1, 'started')

GO

--
--  STORED PROCEDURE
--      SelectCurrentSchemaVersion
--
--  DESCRIPTION
--      Selects the current completed schema version
--
--  RETURNS
--      The current version as a result set
--
CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
AS
BEGIN
    SET NOCOUNT ON

    SELECT MAX(Version)
    FROM SchemaVersion
    WHERE Status = 'complete'
END
GO

--
--  STORED PROCEDURE
--      UpsertSchemaVersion
--
--  DESCRIPTION
--      Creates or updates a new schema version entry
--
--  PARAMETERS
--      @version
--          * The version number
--      @status
--          * The status of the version
--
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


/*************************************************************
    Model tables
**************************************************************/

CREATE TABLE dbo.SearchParam
(
    SearchParamId smallint IDENTITY(1,1) NOT NULL,
    Uri varchar(128) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_SearchParam ON dbo.SearchParam
(
    Uri
)

CREATE TABLE dbo.ResourceType
(
    ResourceTypeId smallint IDENTITY(1,1) NOT NULL,
    Name nvarchar(50) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ResourceType on dbo.ResourceType
(
    Name
)

-- Create System and QuantityCode tables

CREATE TABLE dbo.System
(
    SystemId int IDENTITY(1,1) NOT NULL,
    Value nvarchar(256) NOT NULL,
)

CREATE UNIQUE CLUSTERED INDEX IXC_System ON dbo.System
(
    Value
)

CREATE TABLE dbo.QuantityCode
(
    QuantityCodeId int IDENTITY(1,1) NOT NULL,
    Value nvarchar(256) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_QuantityCode on dbo.QuantityCode
(
    Value
)

/*************************************************************
    Resource table
**************************************************************/

CREATE TABLE dbo.Resource
(
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) NOT NULL,
    Version int NOT NULL,
    IsHistory bit NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    LastUpdated datetime2(7) NOT NULL,
    IsDeleted bit NOT NULL,
    RequestMethod varchar(10) NULL,
    RawResource varbinary(max) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.Resource (
    ResourceSurrogateId
)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource (
    ResourceTypeId,
    ResourceId,
    Version
)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource (
    ResourceTypeId,
    ResourceId
)
INCLUDE (Version)
WHERE IsHistory = 0

GO

CREATE SEQUENCE dbo.ResourceSurrogateIdSequence
        AS BIGINT
        START WITH 0
        INCREMENT BY 1
        NO CYCLE
        CACHE 50
GO

/*************************************************************
    Stored procedures for creating and deleting
**************************************************************/

--
-- STORED PROCEDURE
--     UpsertResource
--
-- DESCRIPTION
--     Creates or updates (including marking deleted) a FHIR resource
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceid
--         * The resource ID (must be the same as the in the resource itself)
--     @allowCreate
--         * If false, an error is thrown if the resource does not already exist
--     @isDeleted
--         * Whether this resource marks the resource as deleted
--     @updatedDateTime
--         * The last modified time in the resource
--     @keepHistory
--         * Whether the existing version of the resource should be preserved
--     @requestMethod
--         * The HTTP method/verb used for the request
--     @rawResource
--         * A compressed UTF16-encoded JSON document
--
-- RETURN VALUE
--         The version of the resource as a result set. Will be empty if no insertion was done.
--
CREATE PROCEDURE dbo.UpsertResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @updatedDateTime datetimeoffset(7),
    @keepHistory bit,
    @requestMethod varchar(10),
    @rawResource varbinary(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @previousVersion TABLE(
        ResourceSurrogateId bigint NOT NULL,
        Version int NOT NULL);

    if (@keepHistory = 1) BEGIN
        -- Preserve the existing version, marking it as history
        UPDATE dbo.Resource WITH (UPDLOCK, HOLDLOCK)
        SET IsHistory = 1
        OUTPUT inserted.ResourceSurrogateId,
                inserted.Version
        INTO @previousVersion
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0 AND (@isDeleted = 0 OR IsDeleted = 0)
    END
    ELSE BEGIN
        -- Delete the previous version
        DELETE FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
        OUTPUT deleted.ResourceSurrogateId,
                deleted.Version
        INTO @previousVersion
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0 AND (@isDeleted = 0 OR IsDeleted = 0)
    END

    DECLARE @version int;

    if (@@ROWCOUNT = 0) BEGIN
        IF (@isDeleted = 1) BEGIN
            -- Either a previous version does not exist or it is already an "IsDeleted" version
            COMMIT TRANSACTION
            RETURN
        END

        IF (@allowCreate = 0) BEGIN
            THROW 50404, 'Resource does not exist and create is not allowed', 1;
        END

        SET @version = 1
    END
    ELSE BEGIN
        -- There is a previous version
        SET @version = (select (Version + 1) from @previousVersion)
    END


    IF (@etag IS NOT NULL AND @etag <> (@version - 1)) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    DECLARE @resourceSurrogateId bigint = NEXT VALUE FOR dbo.ResourceSurrogateIdSequence

    INSERT INTO dbo.Resource
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, LastUpdated, IsDeleted, RequestMethod, RawResource)
    VALUES
        (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, CONVERT(datetime2(7), @updatedDateTime), @isDeleted, @requestMethod, @rawResource)

    select @version

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     ReadResource
--
-- DESCRIPTION
--     Reads a single resource, optionally a specific version of the resource.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID
--     @version
--         * A specific version of the resource. If null, returns the latest version.
-- RETURN VALUE
--         A result set with 0 or 1 rows.
--
CREATE PROCEDURE dbo.ReadResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @version int = NULL
AS
    SET NOCOUNT ON

    IF (@version IS NULL) BEGIN
        SELECT Version, LastUpdated, IsDeleted, IsHistory, RawResource 
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0
    END
    ELSE BEGIN
        SELECT Version, LastUpdated, IsDeleted, IsHistory, RawResource 
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND Version = @version
    END
GO

--
-- STORED PROCEDURE
--     Reads a single resource
--
-- DESCRIPTION
--     Permanently deletes all data related to a resource.
--     Data remains recoverable from the transaction log, however.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as in the resource itself)
--
CREATE PROCEDURE dbo.HardDeleteResource
    @resourceTypeId smallint,
    @resourceId varchar(64)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DELETE FROM dbo.Resource
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId

    COMMIT TRANSACTION
GO
