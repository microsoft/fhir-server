/*************************************************************
    This migration introduces table partitioning by ResourceTypeId on the Resource and all search parameter tables.
    The migration is "online" meaning the server is fully available during the upgrade, but it can be very time-consuming.
    For reference, a database with 50 million synthea resources took around 10 hours to complete on Azure SQL.
**************************************************************/

SET NOCOUNT ON

/*************************************************************
    Migration progress
**************************************************************/

IF NOT EXISTS (
    SELECT * 
    FROM sys.tables
    WHERE name = 'SchemaMigrationProgress')
BEGIN
    CREATE TABLE dbo.SchemaMigrationProgress
    (
        Timestamp datetime2(3) default CURRENT_TIMESTAMP,
        Message nvarchar(max)
    )

END
GO

CREATE OR ALTER PROCEDURE dbo.LogSchemaMigrationProgress
    @message varchar(max)
AS
    INSERT INTO dbo.SchemaMigrationProgress (Message) VALUES (@message)
GO

EXEC dbo.LogSchemaMigrationProgress 'Beginning migration to version 9'

/*************************************************************
    Update stored procedures first. They are backwards-compatible with
    the previous schema version and are required once the indexes become partitioned.
**************************************************************/

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
ALTER PROCEDURE dbo.UpsertResource_3
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
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            -- Set the indexes for this resource as history.
            -- Note there is no IsHistory column on ResourceWriteClaim since we do not query it.

            UPDATE dbo.CompartmentAssignment
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenText
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.StringSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.UriSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.NumberSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.QuantitySearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.DateTimeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenDateTimeCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenQuantityCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenStringCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenNumberNumberCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

        END
        ELSE BEGIN

            -- Not keeping history. Delete the current resource and all associated indexes.

            DELETE FROM dbo.Resource
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ResourceWriteClaim
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.CompartmentAssignment
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenText
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.StringSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.UriSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.NumberSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.QuantitySearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.DateTimeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceTokenCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenTokenCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenDateTimeCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenQuantityCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenStringCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

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
--     Deletes a single resource
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
ALTER PROCEDURE dbo.HardDeleteResource
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

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenText
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    COMMIT TRANSACTION
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
ALTER PROCEDURE dbo.ReindexResource
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
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    -- First, delete all the resource's indices.
    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenText
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

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
ALTER PROCEDURE dbo.BulkReindexResources
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
        ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId AND resourceInDB.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    -- First, delete all the indices of the resources to reindex.
    DELETE searchIndex FROM dbo.ResourceWriteClaim searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.CompartmentAssignment searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.ReferenceSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenText searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.StringSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.UriSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.NumberSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.QuantitySearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.DateTimeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.ReferenceTokenCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenTokenCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenDateTimeCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenQuantityCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenStringCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenNumberNumberCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

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


/*************************************************************
    Partitioning function and scheme
**************************************************************/

IF NOT EXISTS (SELECT *
               FROM  sys.partition_functions
               WHERE name = N'PartitionFunction_ResourceTypeId')
BEGIN
    CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId (smallint) 
    AS RANGE RIGHT FOR VALUES (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150);
END

IF NOT EXISTS (SELECT *
               FROM  sys.partition_schemes
               WHERE name = N'PartitionScheme_ResourceTypeId')
BEGIN
    CREATE PARTITION SCHEME PartitionScheme_ResourceTypeId 
    AS PARTITION PartitionFunction_ResourceTypeId ALL TO ([PRIMARY]);
END

/*************************************************************
    Resource table
**************************************************************/

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_Resource'

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_Resource_ResourceTypeId_ResourceId_Version'

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_Resource_ResourceTypeId_ResourceId'

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource
(
    ResourceTypeId,
    ResourceId
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
WHERE IsHistory = 0
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_Resource_ResourceTypeId_ResourceSurrgateId'

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsDeleted = 0
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

IF NOT EXISTS (
    SELECT * 
    FROM sys.indexes 
    WHERE [name] = 'IX_Resource_ResourceSurrogateId')
BEGIN

    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_Resource_ResourceSurrogateId'

    CREATE NONCLUSTERED INDEX IX_Resource_ResourceSurrogateId ON dbo.Resource
    (
        ResourceSurrogateId
    )
    ON [Primary]

END

/*************************************************************
    Compartments
**************************************************************/

ALTER TABLE dbo.CompartmentAssignment SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_CompartmentAssignment'

CREATE CLUSTERED INDEX IXC_CompartmentAssignment
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    ResourceSurrogateId,
    CompartmentTypeId,
    ReferenceResourceId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId'

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    CompartmentTypeId,
    ReferenceResourceId,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Reference Search Param
**************************************************************/

ALTER TABLE dbo.ReferenceSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_ReferenceSearchParam'

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion'

CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId,
    ReferenceResourceTypeId,
    BaseUri,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceVersion
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token Search Param
**************************************************************/

ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenSearchParam'

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenSeachParam_SearchParamId_Code_SystemId'

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token Text
**************************************************************/

ALTER TABLE dbo.TokenText SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenText'

CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenText_SearchParamId_Text'

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    String Search Param
**************************************************************/

ALTER TABLE dbo.StringSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_StringSearchParam'

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_StringSearchParam_SearchParamId_Text'

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_Text
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    TextOverflow -- will not be needed when all servers are targeting at least this version.
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_StringSearchParam_SearchParamId_TextWithOverflow'

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND TextOverflow IS NOT NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    URI Search Param
**************************************************************/

ALTER TABLE dbo.UriSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_UriSearchParam'

CREATE CLUSTERED INDEX IXC_UriSearchParam
ON dbo.UriSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_UriSearchParam_SearchParamId_Uri'

CREATE NONCLUSTERED INDEX IX_UriSearchParam_SearchParamId_Uri
ON dbo.UriSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Uri,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Number Search Param
**************************************************************/

ALTER TABLE dbo.NumberSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_NumberSearchParam'

CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_NumberSearchParam_SearchParamId_SingleValue'

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    SingleValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_NumberSearchParam_SearchParamId_LowValue_HighValue'

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_NumberSearchParam_SearchParamId_HighValue_LowValue'

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Quantity Search Param
**************************************************************/

ALTER TABLE dbo.QuantitySearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_QuantitySearchParam'

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue'

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    SingleValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue'

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue'

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Date Search Param
**************************************************************/

ALTER TABLE dbo.DateTimeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_DateTimeSearchParam'

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime'

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsLongerThanADay
)
WHERE IsHistory = 0
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime'

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsLongerThanADay
)
WHERE IsHistory = 0
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long'

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long'

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Reference$Token Composite Search Param
**************************************************************/

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_ReferenceTokenCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2'

CREATE NONCLUSTERED INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceTypeId1,
    BaseUri1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token$Token Composite Search Param
**************************************************************/

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenTokenCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenTokenCompositeSearchParam_Code1_Code2'

CREATE NONCLUSTERED INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token$DateTime Composite Search Param
**************************************************************/

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenDateTimeCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2'

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)

WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2'

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long'

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)

WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long'

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token$Quantity Composite Search Param
**************************************************************/

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenQuantityCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2'

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    SingleValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND SingleValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2'

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2'

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    HighValue2,
    LowValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token$String Composite Search Param
**************************************************************/

ALTER TABLE dbo.TokenStringCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenStringCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
ON dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2'

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    TextOverflow2 -- will not be needed when all servers are targeting at least this version.
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow'

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow
ON dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND TextOverflow2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

/*************************************************************
    Token$Number$Number Composite Search Param
**************************************************************/

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenNumberNumberCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2'

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    SingleValue2,
    SingleValue3,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3'

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    LowValue3,
    HighValue3,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 1
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

EXEC dbo.LogSchemaMigrationProgress 'Completed migration to version 9'
