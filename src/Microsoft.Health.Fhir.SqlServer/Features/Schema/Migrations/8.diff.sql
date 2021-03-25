/*************************************************************
    Resource table
**************************************************************/

IF NOT EXISTS (SELECT 'X' FROM SYS.COLUMNS WHERE OBJECT_ID = OBJECT_ID(N'Resource') AND NAME = 'SearchParamHash')
BEGIN
ALTER TABLE dbo.Resource
ADD
    SearchParamHash varchar(64) NULL
END

GO

DROP PROCEDURE IF EXISTS dbo.UpsertResource;

IF TYPE_ID(N'BulkResourceWriteClaimTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkResourceWriteClaimTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ClaimTypeId tinyint NOT NULL,
    ClaimValue nvarchar(128) NOT NULL
)
END

IF TYPE_ID(N'BulkCompartmentAssignmentTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkCompartmentAssignmentTableType_1 AS TABLE
(
    Offset int NOT NULL,
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
)
END

IF TYPE_ID(N'BulkReferenceSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkReferenceSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL
)
END

IF TYPE_ID(N'BulkTokenSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)
END

IF TYPE_ID(N'BulkTokenTextTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenTextTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL
)
END

IF TYPE_ID(N'BulkStringSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkStringSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)
END

IF TYPE_ID(N'BulkUriSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkUriSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
)
END

IF TYPE_ID(N'BulkNumberSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkNumberSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NULL,
    HighValue decimal(18,6) NULL
)
END

IF TYPE_ID(N'BulkQuantitySearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkQuantitySearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NULL,
    HighValue decimal(18,6) NULL
)
END

IF TYPE_ID(N'BulkDateTimeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkDateTimeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetimeoffset(7) NOT NULL,
    EndDateTime datetimeoffset(7) NOT NULL,
    IsLongerThanADay bit NOT NULL
)
END

IF TYPE_ID(N'BulkReferenceTokenCompositeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkReferenceTokenCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)
END

IF TYPE_ID(N'BulkTokenTokenCompositeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenTokenCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)
END

IF TYPE_ID(N'BulkTokenDateTimeCompositeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2 datetimeoffset(7) NOT NULL,
    EndDateTime2 datetimeoffset(7) NOT NULL,
    IsLongerThanADay2 bit NOT NULL
)
END

IF TYPE_ID(N'BulkTokenQuantityCompositeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    QuantityCodeId2 int NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL
)
END

IF TYPE_ID(N'BulkTokenStringCompositeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenStringCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)
END

IF TYPE_ID(N'BulkTokenNumberNumberCompositeSearchParamTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    SingleValue3 decimal(18,6) NULL,
    LowValue3 decimal(18,6) NULL,
    HighValue3 decimal(18,6) NULL,
    HasRange bit NOT NULL
)
END

GO

/*************************************************************
    Stored procedures for creating and deleting
**************************************************************/

--
-- STORED PROCEDURE
--     UpsertResource_3
--
-- DESCRIPTION
--     Creates or updates (including marking deleted) a FHIR resource
--
-- PARAMETERS
--     @baseResourceSurrogateId
--         * A bigint to which a value between [0, 80000) is added, forming a unique ResourceSurrogateId.
--         * This value should be the current UTC datetime, truncated to millisecond precision, with its 100ns ticks component bitshifted left by 3.
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as the in the resource itself)
--     @etag
--         * If specified, the version of the resource to update
--     @allowCreate
--         * If false, an error is thrown if the resource does not already exist
--     @isDeleted
--         * Whether this resource marks the resource as deleted
--     @keepHistory
--         * Whether the existing version of the resource should be preserved
--     @requestMethod
--         * The HTTP method/verb used for the request
--     @searchParamHash
--          * A hash of the resource's latest indexed search parameters
--     @rawResource
--         * A compressed UTF16-encoded JSON document
--     @resourceWriteClaims
--         * Claims on the principal that performed the write
--     @compartmentAssignments
--         * Compartments that the resource is part of
--     @referenceSearchParams
--         * Extracted reference search params
--     @tokenSearchParams
--         * Extracted token search params
--     @tokenTextSearchParams
--         * The text representation of extracted token search params
--     @stringSearchParams
--         * Extracted string search params
--     @numberSearchParams
--         * Extracted number search params
--     @quantitySearchParams
--         * Extracted quantity search params
--     @uriSearchParams
--         * Extracted URI search params
--     @dateTimeSearchParms
--         * Extracted datetime search params
--     @referenceTokenCompositeSearchParams
--         * Extracted reference$token search params
--     @tokenTokenCompositeSearchParams
--         * Extracted token$token tokensearch params
--     @tokenDateTimeCompositeSearchParams
--         * Extracted token$datetime search params
--     @tokenQuantityCompositeSearchParams
--         * Extracted token$quantity search params
--     @tokenStringCompositeSearchParams
--         * Extracted token$string search params
--     @tokenNumberNumberCompositeSearchParams
--         * Extracted token$number$number search params
--
-- RETURN VALUE
--         The version of the resource as a result set. Will be empty if no insertion was done.
--
CREATE OR ALTER PROCEDURE dbo.UpsertResource_3
    @baseResourceSurrogateId bigint,
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @keepHistory bit,
    @requestMethod varchar(10),
    @searchParamHash varchar(64),
    @rawResource varbinary(max),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    -- variables for the existing version of the resource that will be replaced
    DECLARE @previousResourceSurrogateId bigint
    DECLARE @previousVersion bigint
    DECLARE @previousIsDeleted bit

    -- This should place a range lock on a row in the IX_Resource_ResourceTypeId_ResourceId nonclustered filtered index
    SELECT @previousResourceSurrogateId = ResourceSurrogateId, @previousVersion = Version, @previousIsDeleted = IsDeleted
    FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0

    IF (@etag IS NOT NULL AND @etag <> @previousVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    DECLARE @version int -- the version of the resource being written

    IF (@previousResourceSurrogateId IS NULL) BEGIN
        -- There is no previous version of this resource

        IF (@isDeleted = 1) BEGIN
            -- Don't bother marking the resource as deleted since it already does not exist.
            COMMIT TRANSACTION
            RETURN
        END

        IF (@etag IS NOT NULL) BEGIN
        -- You can't update a resource with a specified version if the resource does not exist
            THROW 50404, 'Resource with specified version not found', 1;
        END

        IF (@allowCreate = 0) BEGIN
            THROW 50405, 'Resource does not exist and create is not allowed', 1;
        END

        SET @version = 1
    END
    ELSE BEGIN
        -- There is a previous version

        IF (@isDeleted = 1 AND @previousIsDeleted = 1) BEGIN
            -- Already deleted - don't create a new version
            COMMIT TRANSACTION
            RETURN
        END

        SET @version = @previousVersion + 1

        IF (@keepHistory = 1) BEGIN

            -- Set the existing resource as history
            UPDATE dbo.Resource
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            -- Set the indexes for this resource as history.
            -- Note there is no IsHistory column on ResourceWriteClaim since we do not query it.

            UPDATE dbo.CompartmentAssignment
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceSearchParam
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

            UPDATE dbo.UriSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.NumberSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.QuantitySearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.DateTimeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenDateTimeCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenQuantityCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenStringCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenNumberNumberCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

        END
        ELSE BEGIN

            -- Not keeping history. Delete the current resource and all associated indexes.

            DELETE FROM dbo.Resource
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ResourceWriteClaim
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.CompartmentAssignment
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenText
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.StringSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.UriSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.NumberSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.QuantitySearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.DateTimeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceTokenCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenTokenCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenDateTimeCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenQuantityCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenStringCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

        END
    END

    DECLARE @resourceSurrogateId bigint = @baseResourceSurrogateId + (NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence)
    DECLARE @isRawResourceMetaSet bit

    IF (@version = 1) BEGIN SET @isRawResourceMetaSet = 1 END ELSE BEGIN SET @isRawResourceMetaSet = 0 END

    INSERT INTO dbo.Resource
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
    VALUES
        (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, @isDeleted, @requestMethod, @rawResource, @isRawResourceMetaSet, @searchParamHash)

    INSERT INTO dbo.ResourceWriteClaim
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT @resourceSurrogateId, ClaimTypeId, ClaimValue
    FROM @resourceWriteClaims

    INSERT INTO dbo.CompartmentAssignment
        (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId, 0
    FROM @compartmentAssignments

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, 0
    FROM @referenceSearchParams

    INSERT INTO dbo.TokenSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, Code, 0
    FROM @tokenSearchParams

    INSERT INTO dbo.TokenText
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, 0
    FROM @tokenTextSearchParams

    INSERT INTO dbo.StringSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0
    FROM @stringSearchParams

    INSERT INTO dbo.UriSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Uri, 0
    FROM @uriSearchParams

    INSERT INTO dbo.NumberSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, 0
    FROM @numberSearchParams

    INSERT INTO dbo.QuantitySearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, 0
    FROM @quantitySearchParams

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, 0
    FROM @dateTimeSearchParms

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, 0
    FROM @referenceTokenCompositeSearchParams

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, 0
    FROM @tokenTokenCompositeSearchParams

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, 0
    FROM @tokenDateTimeCompositeSearchParams

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, 0
    FROM @tokenNumberNumberCompositeSearchParams

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
ALTER PROCEDURE dbo.ReadResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @version int = NULL
AS
    SET NOCOUNT ON

    IF (@version IS NULL) BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0
    END
    ELSE BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND Version = @version
    END
GO

--
-- STORED PROCEDURE
--     UpsertSearchParams
--
-- DESCRIPTION
--     Given a set of search parameters, creates or updates the parameters.
--
-- PARAMETERS
--     @searchParams
--         * The updated existing search parameters or the new search parameters
--
-- RETURN VALUE
--     The IDs and URIs of the search parameters that were inserted (not updated).
--
ALTER PROCEDURE dbo.UpsertSearchParams
    @searchParams dbo.SearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()

    DECLARE @summaryOfChanges TABLE(Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL, Action varchar(20) NOT NULL)

    -- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsert.
    MERGE INTO dbo.SearchParam WITH (TABLOCKX) AS target
    USING @searchParams AS source
    ON target.Uri = source.Uri
    WHEN MATCHED THEN
        UPDATE
            SET Status = source.Status, LastUpdated = @lastUpdated, IsPartiallySupported = source.IsPartiallySupported
    WHEN NOT MATCHED BY target THEN
        INSERT
            (Uri, Status, LastUpdated, IsPartiallySupported)
            VALUES (source.Uri, source.Status, @lastUpdated, source.IsPartiallySupported)
    OUTPUT source.Uri, $action INTO @summaryOfChanges;

    SELECT SearchParamId, SearchParam.Uri
    FROM dbo.SearchParam searchParam
    INNER JOIN @summaryOfChanges upsertedSearchParam
    ON searchParam.Uri = upsertedSearchParam.Uri
    WHERE upsertedSearchParam.Action = 'INSERT'

    COMMIT TRANSACTION
GO

/*************************************************************
    Reindex Job
**************************************************************/
IF NOT EXISTS (SELECT 'X' FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ReindexJob')
BEGIN
CREATE TABLE dbo.ReindexJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)
END

IF NOT EXISTS (SELECT 'X' FROM SYS.INDEXES WHERE name = 'IXC_ReindexJob' AND OBJECT_ID = OBJECT_ID('ReindexJob'))
BEGIN
CREATE UNIQUE CLUSTERED INDEX IXC_ReindexJob ON dbo.ReindexJob
(
    Id
)
END

GO

/*************************************************************
    Stored procedures for reindexing
**************************************************************/
--
-- STORED PROCEDURE
--     Creates an reindex job.
--
-- DESCRIPTION
--     Creates a new row to the ReindexJob table, adding a new job to the queue of jobs to be processed.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record
--     @status
--         * The status of the reindex job
--     @rawJobRecord
--         * A JSON document
--
-- RETURN VALUE
--     The row version of the created reindex job.
--
CREATE OR ALTER PROCEDURE dbo.CreateReindexJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    INSERT INTO dbo.ReindexJob
        (Id, Status, HeartbeatDateTime, RawJobRecord)
    VALUES
        (@id, @status, @heartbeatDateTime, @rawJobRecord)

    SELECT CAST(MIN_ACTIVE_ROWVERSION() AS INT)

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Gets an reindex job given its ID.
--
-- DESCRIPTION
--     Retrieves the reindex job record from the ReindexJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record to retrieve
--
-- RETURN VALUE
--     The matching reindex job.
--
CREATE OR ALTER PROCEDURE dbo.GetReindexJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ReindexJob
    WHERE Id = @id
GO

--
-- STORED PROCEDURE
--     Updates a reindex job.
--
-- DESCRIPTION
--     Modifies an existing job in the ReindexJob table.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record
--     @status
--         * The status of the reindex job
--     @rawJobRecord
--         * A JSON document
--     @jobVersion
--         * The version of the job to update must match this
--
-- RETURN VALUE
--     The row version of the updated reindex job.
--
CREATE OR ALTER PROCEDURE dbo.UpdateReindexJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max),
    @jobVersion binary(8)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @currentJobVersion binary(8)

    -- Acquire and hold an update lock on a row in the ReindexJob table for the entire transaction.
    -- This ensures the version check and update occur atomically.
    SELECT @currentJobVersion = JobVersion
    FROM dbo.ReindexJob WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = @id

    IF (@currentJobVersion IS NULL) BEGIN
        THROW 50404, 'Reindex job record not found', 1;
    END

    IF (@jobVersion <> @currentJobVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.ReindexJob
    SET Status = @status, HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = @rawJobRecord
    WHERE Id = @id

    SELECT MIN_ACTIVE_ROWVERSION()

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Acquires reindex jobs.
--
-- DESCRIPTION
--     Timestamps the available reindex jobs and sets their statuses to running.
--
-- PARAMETERS
--     @jobHeartbeatTimeoutThresholdInSeconds
--         * The number of seconds that must pass before a reindex job is considered stale
--     @maximumNumberOfConcurrentJobsAllowed
--         * The maximum number of running jobs we can have at once
--
-- RETURN VALUE
--     The updated jobs that are now running.
--
CREATE OR ALTER PROCEDURE dbo.AcquireReindexJobs
    @jobHeartbeatTimeoutThresholdInSeconds bigint,
    @maximumNumberOfConcurrentJobsAllowed int
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    -- We will consider a job to be stale if its timestamp is smaller than or equal to this.
    DECLARE @expirationDateTime dateTime2(7)
    SELECT @expirationDateTime = DATEADD(second, -@jobHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME())

    -- Get the number of jobs that are running and not stale.
    -- Acquire and hold an exclusive table lock for the entire transaction to prevent jobs from being created, updated or deleted during acquisitions.
    DECLARE @numberOfRunningJobs int
    SELECT @numberOfRunningJobs = COUNT(*) FROM dbo.ReindexJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > @expirationDateTime

    -- Determine how many available jobs we can pick up.
    DECLARE @limit int = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;

    IF (@limit > 0) BEGIN

        DECLARE @availableJobs TABLE (Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL, JobVersion binary(8) NOT NULL)

        -- Get the available jobs, which are reindex jobs that are queued or stale.
        -- Older jobs will be prioritized over newer ones.
        INSERT INTO @availableJobs
        SELECT TOP(@limit) Id, JobVersion
        FROM dbo.ReindexJob
        WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime

        DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

        -- Update each available job's status to running both in the reindex table's status column and in the raw reindex job record JSON.
        UPDATE dbo.ReindexJob
        SET Status = 'Running', HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = JSON_MODIFY(RawJobRecord,'$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM dbo.ReindexJob job INNER JOIN @availableJobs availableJob ON job.Id = availableJob.Id AND job.JobVersion = availableJob.JobVersion

    END

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Checks if there are any active reindex jobs.
--
-- DESCRIPTION
--     Queries the datastore for any reindex job documents with a status of running, queued or paused.
--
-- RETURN VALUE
--     The job IDs of any active reindex jobs.
--
CREATE OR ALTER PROCEDURE dbo.CheckActiveReindexJobs
AS
    SET NOCOUNT ON

    SELECT Id
    FROM dbo.ReindexJob
    WHERE Status = 'Running' OR Status = 'Queued' OR Status = 'Paused'
GO

--
-- STORED PROCEDURE
--     ReindexResource
--
-- DESCRIPTION
--     Updates the search indices of a given resource
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as the in the resource itself)
--     @etag
--         * If specified, the version of the resource to update
--     @searchParamHash
--          * A hash of the resource's latest indexed search parameters
--     @resourceWriteClaims
--         * Claims on the principal that performed the write
--     @compartmentAssignments
--         * Compartments that the resource is part of
--     @referenceSearchParams
--         * Extracted reference search params
--     @tokenSearchParams
--         * Extracted token search params
--     @tokenTextSearchParams
--         * The text representation of extracted token search params
--     @stringSearchParams
--         * Extracted string search params
--     @numberSearchParams
--         * Extracted number search params
--     @quantitySearchParams
--         * Extracted quantity search params
--     @uriSearchParams
--         * Extracted URI search params
--     @dateTimeSearchParms
--         * Extracted datetime search params
--     @referenceTokenCompositeSearchParams
--         * Extracted reference$token search params
--     @tokenTokenCompositeSearchParams
--         * Extracted token$token tokensearch params
--     @tokenDateTimeCompositeSearchParams
--         * Extracted token$datetime search params
--     @tokenQuantityCompositeSearchParams
--         * Extracted token$quantity search params
--     @tokenStringCompositeSearchParams
--         * Extracted token$string search params
--     @tokenNumberNumberCompositeSearchParams
--         * Extracted token$number$number search params
--
CREATE OR ALTER PROCEDURE dbo.ReindexResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @searchParamHash varchar(64),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @resourceSurrogateId bigint
    DECLARE @version bigint

    -- This should place a range lock on a row in the IX_Resource_ResourceTypeId_ResourceId nonclustered filtered index
    SELECT @resourceSurrogateId = ResourceSurrogateId, @version = Version
    FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0

    IF (@etag IS NOT NULL AND @etag <> @version) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    IF (@resourceSurrogateId IS NULL) BEGIN
        -- You can't reindex a resource if the resource does not exist
        THROW 50404, 'Resource not found', 1;
    END

    UPDATE dbo.Resource
    SET SearchParamHash = @searchParamHash
    WHERE ResourceSurrogateId = @resourceSurrogateId

    -- First, delete all the resource's indices.
    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenText
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceSurrogateId = @resourceSurrogateId

    -- Next, insert all the new indices.
    INSERT INTO dbo.ResourceWriteClaim
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT @resourceSurrogateId, ClaimTypeId, ClaimValue
    FROM @resourceWriteClaims

    INSERT INTO dbo.CompartmentAssignment
        (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId, 0
    FROM @compartmentAssignments

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, 0
    FROM @referenceSearchParams

    INSERT INTO dbo.TokenSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, Code, 0
    FROM @tokenSearchParams

    INSERT INTO dbo.TokenText
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, 0
    FROM @tokenTextSearchParams

    INSERT INTO dbo.StringSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0
    FROM @stringSearchParams

    INSERT INTO dbo.UriSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Uri, 0
    FROM @uriSearchParams

    INSERT INTO dbo.NumberSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, 0
    FROM @numberSearchParams

    INSERT INTO dbo.QuantitySearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, 0
    FROM @quantitySearchParams

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, 0
    FROM @dateTimeSearchParms

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, 0
    FROM @referenceTokenCompositeSearchParams

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, 0
    FROM @tokenTokenCompositeSearchParams

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, 0
    FROM @tokenDateTimeCompositeSearchParams

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, 0
    FROM @tokenNumberNumberCompositeSearchParams

    COMMIT TRANSACTION
GO

IF TYPE_ID(N'BulkReindexResourceTableType_1') IS NULL
BEGIN
CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag int NULL,
    SearchParamHash varchar(64) NOT NULL
)
END

GO

--
-- STORED PROCEDURE
--     BulkReindexResources
--
-- DESCRIPTION
--     Updates the search indices of a batch of resources
--
-- PARAMETERS
--     @resourcesToReindex
--         * The type IDs, IDs, eTags and hashes of the resources to reindex
--     @resourceWriteClaims
--         * Claims on the principal that performed the write
--     @compartmentAssignments
--         * Compartments that the resource is part of
--     @referenceSearchParams
--         * Extracted reference search params
--     @tokenSearchParams
--         * Extracted token search params
--     @tokenTextSearchParams
--         * The text representation of extracted token search params
--     @stringSearchParams
--         * Extracted string search params
--     @numberSearchParams
--         * Extracted number search params
--     @quantitySearchParams
--         * Extracted quantity search params
--     @uriSearchParams
--         * Extracted URI search params
--     @dateTimeSearchParms
--         * Extracted datetime search params
--     @referenceTokenCompositeSearchParams
--         * Extracted reference$token search params
--     @tokenTokenCompositeSearchParams
--         * Extracted token$token tokensearch params
--     @tokenDateTimeCompositeSearchParams
--         * Extracted token$datetime search params
--     @tokenQuantityCompositeSearchParams
--        * Extracted token$quantity search params
--     @tokenStringCompositeSearchParams
--         * Extracted token$string search params
--     @tokenNumberNumberCompositeSearchParams
--         * Extracted token$number$number search params
--
CREATE OR ALTER PROCEDURE dbo.BulkReindexResources
    @resourcesToReindex dbo.BulkReindexResourceTableType_1 READONLY,
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @computedValues TABLE
    (
        Offset int NOT NULL,
        ResourceTypeId smallint NOT NULL,
        VersionProvided bigint NULL,
        SearchParamHash varchar(64) NOT NULL,
        ResourceSurrogateId bigint NULL,
        VersionInDatabase bigint NULL
    )

    INSERT INTO @computedValues
    SELECT
        resourceToReindex.Offset,
        resourceToReindex.ResourceTypeId,
        resourceToReindex.ETag,
        resourceToReindex.SearchParamHash,
        resourceInDB.ResourceSurrogateId,
        resourceInDB.Version
    FROM @resourcesToReindex resourceToReindex
    LEFT OUTER JOIN dbo.Resource resourceInDB WITH (UPDLOCK, INDEX(IX_Resource_ResourceTypeId_ResourceId))
        ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
            AND resourceInDB.ResourceId = resourceToReindex.ResourceId
            AND resourceInDB.IsHistory = 0

    DECLARE @resourcesNotInDatabase int
    SET @resourcesNotInDatabase = (SELECT COUNT(*) FROM @computedValues WHERE ResourceSurrogateId IS NULL)

    IF (@resourcesNotInDatabase > 0) BEGIN
        -- We can't reindex a resource if the resource does not exist
        THROW 50404, 'One or more resources not found', 1;
    END

    DECLARE @versionDiff int
    SET @versionDiff = (SELECT COUNT(*) FROM @computedValues WHERE VersionProvided IS NOT NULL AND VersionProvided <> VersionInDatabase)

    IF (@versionDiff > 0) BEGIN
        -- The resource has been updated since the reindex job kicked off
        THROW 50412, 'Precondition failed', 1;
    END

    -- Update the search parameter hash value in the main resource table
    UPDATE resourceInDB
    SET resourceInDB.SearchParamHash = resourceToReindex.SearchParamHash
    FROM @computedValues resourceToReindex
    INNER JOIN dbo.Resource resourceInDB
        ON resourceInDB.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    -- First, delete all the indices of the resources to reindex.
    DELETE searchIndex FROM dbo.ResourceWriteClaim searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.CompartmentAssignment searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.ReferenceSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenText searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.StringSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.UriSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.NumberSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.QuantitySearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.DateTimeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.ReferenceTokenCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenTokenCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenDateTimeCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenQuantityCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenStringCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenNumberNumberCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    -- Next, insert all the new indices.
    INSERT INTO dbo.ResourceWriteClaim
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT DISTINCT resourceToReindex.ResourceSurrogateId, searchIndex.ClaimTypeId, searchIndex.ClaimValue
    FROM @resourceWriteClaims searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.CompartmentAssignment
        (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.CompartmentTypeId, searchIndex.ReferenceResourceId, 0
    FROM @compartmentAssignments searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.BaseUri, searchIndex.ReferenceResourceTypeId, searchIndex.ReferenceResourceId, searchIndex.ReferenceResourceVersion, 0
    FROM @referenceSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId, searchIndex.Code, 0
    FROM @tokenSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenText
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Text, 0
    FROM @tokenTextSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.StringSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Text, searchIndex.TextOverflow, 0
    FROM @stringSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.UriSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Uri, 0
    FROM @uriSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.NumberSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SingleValue, searchIndex.LowValue, searchIndex.HighValue, 0
    FROM @numberSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.QuantitySearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId, searchIndex.QuantityCodeId, searchIndex.SingleValue, searchIndex.LowValue, searchIndex.HighValue, 0
    FROM @quantitySearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.StartDateTime, searchIndex.EndDateTime, searchIndex.IsLongerThanADay, 0
    FROM @dateTimeSearchParms searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.BaseUri1, searchIndex.ReferenceResourceTypeId1, searchIndex.ReferenceResourceId1, searchIndex.ReferenceResourceVersion1, searchIndex.SystemId2, searchIndex.Code2, 0
    FROM @referenceTokenCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.SystemId2, searchIndex.Code2, 0
    FROM @tokenTokenCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.StartDateTime2, searchIndex.EndDateTime2, searchIndex.IsLongerThanADay2, 0
    FROM @tokenDateTimeCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.SingleValue2, searchIndex.SystemId2, searchIndex.QuantityCodeId2, searchIndex.LowValue2, searchIndex.HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.Text2, searchIndex.TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.SingleValue2, searchIndex.LowValue2, searchIndex.HighValue2, searchIndex.SingleValue3, searchIndex.LowValue3, searchIndex.HighValue3, searchIndex.HasRange, 0
    FROM @tokenNumberNumberCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    COMMIT TRANSACTION
GO
