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

/*************************************************************
    Capture claims on write
**************************************************************/

CREATE TABLE dbo.ClaimType
(
    ClaimTypeId tinyint IDENTITY(1,1) NOT NULL,
    Name varchar(128) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Claim on dbo.ClaimType
(
    Name
)

CREATE TYPE dbo.ResourceWriteClaimTableType_1 AS TABLE  
(
    ClaimTypeId tinyint NOT NULL,
    ClaimValue nvarchar(128) NOT NULL
)

CREATE TABLE dbo.ResourceWriteClaim
(
    ResourceSurrogateId bigint NOT NULL,
    ClaimTypeId tinyint NOT NULL,
    ClaimValue nvarchar(128) NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_LastModifiedClaim on dbo.ResourceWriteClaim
(
    ResourceSurrogateId,
    ClaimTypeId
)

/*************************************************************
    Compartments
**************************************************************/

CREATE TABLE dbo.CompartmentType
(
    CompartmentTypeId tinyint IDENTITY(1,1) NOT NULL,
    Name varchar(128) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_CompartmentType on dbo.CompartmentType
(
    Name
)

CREATE TYPE dbo.CompartmentAssignmentTableType_1 AS TABLE  
(
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) NOT NULL
)

CREATE TABLE dbo.CompartmentAssignment
(
    ResourceSurrogateId bigint NOT NULL,
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_CompartmentAssignment 
ON dbo.CompartmentAssignment
(
    ResourceSurrogateId,
    CompartmentTypeId,
    ReferenceResourceId
)

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId 
ON dbo.CompartmentAssignment
(
    CompartmentTypeId,
    ReferenceResourceId
) 
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token Search Param
**************************************************************/

CREATE TYPE dbo.TokenSearchParamTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) NOT NULL
)

CREATE TABLE dbo.TokenSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Code,
    SystemId
)

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId 
ON dbo.TokenSearchParam
(
    SearchParamId,
    Code,
    SystemId
) 
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token Text
**************************************************************/

CREATE TYPE dbo.TokenTextTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL
)

CREATE TABLE dbo.TokenText
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory bit NOT NULL
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceSurrogateId,
    SearchParamId,
    Text
)

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    SearchParamId,
    Text
) 
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    String Search Param
**************************************************************/

CREATE TYPE dbo.StringSearchParamTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL
)

CREATE TABLE dbo.StringSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory bit NOT NULL
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Text
)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_Text
ON dbo.StringSearchParam
(
    SearchParamId,
    Text
) 
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Sequence for generating surrogate IDs for resources
**************************************************************/

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
--     @resourceWriteClaims
--         * claims on the principal that performed the write
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
    @rawResource varbinary(max),
    @resourceWriteClaims dbo.ResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.CompartmentAssignmentTableType_1 READONLY,
    @tokenSearchParams dbo.TokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.TokenTextTableType_1 READONLY,
    @stringSearchParams dbo.StringSearchParamTableType_1 READONLY
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
        DECLARE @previousResourceSurrogateId bigint
        
        SELECT @version = (Version + 1), @previousResourceSurrogateId = ResourceSurrogateId 
        FROM @previousVersion

        IF (@keepHistory = 1) BEGIN

            -- note there is no IsHistory column on ResourceWriteClaim since we do not query it

            UPDATE dbo.CompartmentAssignment
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenText
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.StringSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

        END
        ELSE BEGIN

            DELETE FROM dbo.ResourceWriteClaim
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.CompartmentAssignment
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenText
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.StringSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

        END
    END


    IF (@etag IS NOT NULL AND @etag <> (@version - 1)) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    DECLARE @resourceSurrogateId bigint = NEXT VALUE FOR dbo.ResourceSurrogateIdSequence

    INSERT INTO dbo.Resource
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, LastUpdated, IsDeleted, RequestMethod, RawResource)
    VALUES
        (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, CONVERT(datetime2(7), @updatedDateTime), @isDeleted, @requestMethod, @rawResource)

    INSERT INTO dbo.ResourceWriteClaim 
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT @resourceSurrogateId, ClaimTypeId, ClaimValue 
    FROM @resourceWriteClaims

    INSERT INTO dbo.CompartmentAssignment
        (ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId, 0
    FROM @compartmentAssignments

    INSERT INTO dbo.TokenSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId, Code, 0
    FROM @tokenSearchParams

    INSERT INTO dbo.TokenText
        (ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, Text, 0
    FROM @tokenTextSearchParams

    INSERT INTO dbo.StringSearchParam
        (ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, Text, 0
    FROM @stringSearchParams

    SELECT @version

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

    DECLARE @resourceSurrogateIds TABLE(ResourceSurrogateId bigint NOT NULL)

    DELETE FROM dbo.Resource
    OUTPUT deleted.ResourceSurrogateId
    INTO @resourceSurrogateIds
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId

    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    COMMIT TRANSACTION
GO
