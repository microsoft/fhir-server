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
-- RETURN VALUE
--     The number of resources that failed to reindex due to versioning conflicts.
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

    DECLARE @versionDiff int
    SET @versionDiff = (SELECT COUNT(*) FROM @computedValues WHERE VersionProvided IS NOT NULL AND VersionProvided <> VersionInDatabase)

    IF (@versionDiff > 0) BEGIN
        -- Don't reindex resources that have outdated versions
        DELETE FROM @computedValues
        WHERE  VersionProvided IS NOT NULL AND VersionProvided <> VersionInDatabase
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

    SELECT @versionDiff

    COMMIT TRANSACTION
GO
