SET NOCOUNT ON

IF NOT EXISTS (
    SELECT * 
    FROM sys.tables t
    INNER JOIN sys.all_columns c
    ON t.object_id = c.object_id
    WHERE t.name = 'ReferenceSearchParam'
    AND c.name = 'ReferenceResourceSurrogateId')
BEGIN

    ALTER TABLE dbo.ReferenceSearchParam
        ADD ReferenceResourceSurrogateId bigint NULL
END
GO

IF NOT EXISTS (
    SELECT * 
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic 
    ON i.object_id = ic.object_id 
        AND i.index_id = ic.index_id
    INNER JOIN sys.all_columns c
    ON i.object_id = c.object_id and c.column_id = ic.column_id
    WHERE i.name = 'IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion'
    AND c.name = 'ReferenceResourceSurrogateId')
BEGIN

    -- Add ReferenceResourceSurrogateId as an included column

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
        ReferenceResourceVersion,
        ReferenceResourceSurrogateId
    )
    WHERE IsHistory = 0
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)

END

IF NOT EXISTS (
    SELECT * 
    FROM sys.indexes 
    WHERE name = 'IX_ResourceTypeId_SearchParamId_ReferenceResourceTypeId_ReferenceResourceSurrogateId')
BEGIN

    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ResourceTypeId_SearchParamId_ReferenceResourceTypeId_ReferenceResourceSurrogateId'

    -- used for finding objects of a known type pointed to by ResourceSurrogateId
    CREATE NONCLUSTERED INDEX IX_ResourceTypeId_SearchParamId_ReferenceResourceTypeId_ReferenceResourceSurrogateId
    ON dbo.ReferenceSearchParam
    (
        ResourceTypeId,
        SearchParamId,
        ReferenceResourceTypeId,
        ReferenceResourceSurrogateId
    )
    INCLUDE
    (
        IsHistory,
        BaseUri
    )
    WHERE IsHistory = 0 AND ReferenceResourceSurrogateId IS NOT NULL AND BaseUri IS NULL
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)

END

IF NOT EXISTS (
    SELECT * 
    FROM sys.indexes 
    WHERE name = 'IX_ReferenceResourceTypeId_ReferenceResourceId_SearchParamId_ResourceTypeId_ResourceSurrogateId')
BEGIN

    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ReferenceResourceTypeId_ReferenceResourceId_SearchParamId_ResourceTypeId_ResourceSurrogateId'

    -- used when upserting a resource to find all resources pointing to it.
    CREATE NONCLUSTERED INDEX IX_ReferenceResourceTypeId_ReferenceResourceId_SearchParamId_ResourceTypeId_ResourceSurrogateId
    ON dbo.ReferenceSearchParam
    (
        ReferenceResourceTypeId,
        ReferenceResourceId,
        SearchParamId,
        ResourceTypeId,
        ResourceSurrogateId
    )
    INCLUDE
    (
        IsHistory,
        BaseUri
    )
    WHERE IsHistory = 0 AND BaseUri IS NULL
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId(ReferenceResourceTypeId)

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

    /* Update inbound references. When we support referential integrity, this only needs to be done on update */
    UPDATE dbo.ReferenceSearchParam WITH (HOLDLOCK) -- make sure nobody can insert new entries in this range
        SET ReferenceResourceSurrogateId = CASE @isDeleted WHEN 1 THEN NULL ELSE @resourceSurrogateId END
        WHERE ReferenceResourceTypeId = @resourceTypeId 
            AND ReferenceResourceId = @resourceId
            AND IsHistory = 0
            AND BaseUri IS NULL

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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, ReferenceResourceSurrogateId, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, ref.SearchParamId, ref.BaseUri, ref.ReferenceResourceTypeId, ref.ReferenceResourceId, ref.ReferenceResourceVersion, res.ResourceSurrogateId, 0
    FROM @referenceSearchParams ref
    LEFT OUTER JOIN Resource res WITH (HOLDLOCK) -- lock the range so nobody can update/add to the outgoing targets
        ON ref.ReferenceResourceTypeId = res.ResourceTypeId
            AND ref.ReferenceResourceId = res.ResourceId
            AND res.IsHistory = 0
            AND ref.BaseUri IS NULL

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

    UPDATE dbo.ReferenceSearchParam WITH (HOLDLOCK) -- make sure nobody can insert new entries in this range
    SET ReferenceResourceSurrogateId = NULL
    WHERE ReferenceResourceTypeId = @resourceTypeId 
        AND ReferenceResourceId = @resourceId
        AND IsHistory = 0
        AND BaseUri IS NULL

    COMMIT TRANSACTION
GO