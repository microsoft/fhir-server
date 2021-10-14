/*
We are making the following changes in this version of the schema
-- Adding two bit columns to dbo.StringSearchParam table - IsMin and IsMax
-- Adding two bit columns to dbo.DataTimeSearchParam table - IsMin and IsMax
-- Updating the corresponding table value type to include these two new values
-- Updating the UpsertResources, ReindexResource and BulkReindexResource 
-- stored procedures to handle these two new values.
*/

IF TYPE_ID(N'BulkStringSearchParamTableType_2') IS NULL
BEGIN
    CREATE TYPE dbo.BulkStringSearchParamTableType_2 AS TABLE
    (
        Offset int NOT NULL,
        SearchParamId smallint NOT NULL,
        Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
        TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
        IsMin bit NOT NULL,
        IsMax bit NOT NULL
    )
END

IF NOT EXISTS (SELECT 'X' FROM SYS.COLUMNS WHERE OBJECT_ID = OBJECT_ID(N'StringSearchParam') AND NAME = 'IsMin')
BEGIN
    ALTER TABLE dbo.StringSearchParam
    ADD IsMin bit NOT NULL,
        IsMax bit NOT NULL,
        CONSTRAINT string_IsMin_Constraint DEFAULT 0 FOR IsMin,
        CONSTRAINT string_IsMax_Constraint DEFAULT 0 FOR IsMax;
END

GO

UPDATE dbo.StringSearchParam
SET IsMin = 1
FROM 
(
    SELECT ResourceSurrogateId, SearchParamId, MIN(Text) AS minVal
    FROM dbo.StringSearchParam
    GROUP BY ResourceSurrogateId, SearchParamId
)
AS results
WHERE
(
    dbo.StringSearchParam.ResourceSurrogateId = results.ResourceSurrogateId
    AND dbo.StringSearchParam.SearchParamId = results.SearchParamId
    AND dbo.StringSearchParam.Text = results.minVal
)

UPDATE dbo.StringSearchParam
SET dbo.StringSearchParam.IsMax = 1
FROM 
(
    SELECT ResourceSurrogateId, SearchParamId, MAX(Text) AS maxVal
    FROM dbo.StringSearchParam
    GROUP BY ResourceSurrogateId, SearchParamId
)
AS results
WHERE
(
    dbo.StringSearchParam.ResourceSurrogateId = results.ResourceSurrogateId
    AND dbo.StringSearchParam.SearchParamId = results.SearchParamId
    AND dbo.StringSearchParam.Text = results.maxVal
)

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
    TextOverflow, -- will not be needed when all servers are targeting at least this version.
    IsMin,
    IsMax
)
WHERE IsHistory = 0
WITH 
(
    DATA_COMPRESSION = PAGE,
    DROP_EXISTING = ON,
    ONLINE = ON
)
ON [PartitionScheme_ResourceTypeId](ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND TextOverflow IS NOT NULL
WITH 
(
    DATA_COMPRESSION = PAGE,
    DROP_EXISTING = ON,
    ONLINE = ON
)
ON [PartitionScheme_ResourceTypeId](ResourceTypeId)

IF TYPE_ID(N'BulkDateTimeSearchParamTableType_2') IS NULL
BEGIN
    CREATE TYPE dbo.BulkDateTimeSearchParamTableType_2 AS TABLE
    (
        Offset int NOT NULL,
        SearchParamId smallint NOT NULL,
        StartDateTime datetimeoffset(7) NOT NULL,
        EndDateTime datetimeoffset(7) NOT NULL,
        IsLongerThanADay bit NOT NULL,
        IsMin bit NOT NULL,
        IsMax bit NOT NULL
    )
END

IF NOT EXISTS (SELECT 'X' FROM SYS.COLUMNS WHERE OBJECT_ID = OBJECT_ID(N'DateTimeSearchParam') AND NAME = 'IsMin')
BEGIN
    ALTER TABLE dbo.DateTimeSearchParam
    ADD IsMin bit NOT NULL,
        IsMax bit NOT NULL,
        CONSTRAINT date_IsMin_Constraint DEFAULT 0 FOR IsMin,
        CONSTRAINT date_IsMax_Constraint DEFAULT 0 FOR IsMax;
END

GO

UPDATE dbo.DateTimeSearchParam
SET IsMin = 1
FROM 
(
    SELECT ResourceSurrogateId, SearchParamId, MIN(StartDateTime) AS minVal
    FROM dbo.DateTimeSearchParam
    GROUP BY ResourceSurrogateId, SearchParamId
)
AS results
WHERE
(
    dbo.DateTimeSearchParam.ResourceSurrogateId = results.ResourceSurrogateId
    AND dbo.DateTimeSearchParam.SearchParamId = results.SearchParamId
    AND dbo.DateTimeSearchParam.StartDateTime = results.minVal
)

UPDATE dbo.DateTimeSearchParam
SET IsMax = 1
FROM 
(
    SELECT ResourceSurrogateId, SearchParamId, MAX(EndDateTime) AS maxVal
    FROM dbo.DateTimeSearchParam
    GROUP BY ResourceSurrogateId, SearchParamId
)
AS results
WHERE
(
    dbo.DateTimeSearchParam.ResourceSurrogateId = results.ResourceSurrogateId
    AND dbo.DateTimeSearchParam.SearchParamId = results.SearchParamId
    AND dbo.DateTimeSearchParam.EndDateTime = results.maxVal
)

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
    IsLongerThanADay,
    IsMin,
    IsMax
)
WHERE IsHistory = 0
WITH 
(
    DROP_EXISTING = ON,
    ONLINE = ON
)
ON [PartitionScheme_ResourceTypeId](ResourceTypeId)

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
    IsLongerThanADay,
    IsMin,
    IsMax
)
WHERE IsHistory = 0
WITH 
(
    DROP_EXISTING = ON,
    ONLINE = ON
)
ON [PartitionScheme_ResourceTypeId](ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
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
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
WITH 
(
    DROP_EXISTING = ON,
    ONLINE = ON
)
ON [PartitionScheme_ResourceTypeId](ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
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
    IsMin,
    IsMax
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
WITH 
(
    DROP_EXISTING = ON,
    ONLINE = ON
)
ON [PartitionScheme_ResourceTypeId](ResourceTypeId)

GO

/*************************************************************
    Stored procedures for creating and deleting
**************************************************************/

--
-- STORED PROCEDURE
--     UpsertResource_5
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
--     @isResourceChangeCaptureEnabled
--         * Whether capturing resource change data
--
-- RETURN VALUE
--         The version of the resource as a result set. Will be empty if no insertion was done.
--
CREATE OR ALTER PROCEDURE dbo.UpsertResource_5
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
    @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY,
    @isResourceChangeCaptureEnabled bit = 0
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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0, IsMin, IsMax
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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, 0, IsMin, IsMax
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

    IF (@isResourceChangeCaptureEnabled = 1) BEGIN
        --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
        EXEC dbo.CaptureResourceChanges @isDeleted=@isDeleted, @version=@version, @resourceId=@resourceId, @resourceTypeId=@resourceTypeId
    END

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     ReindexResource_2
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
CREATE OR ALTER PROCEDURE dbo.ReindexResource_2
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @searchParamHash varchar(64),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY,
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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0, IsMin, IsMax
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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, 0, IsMin, IsMax
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
--     BulkReindexResources_2
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
CREATE OR ALTER PROCEDURE dbo.BulkReindexResources_2
    @resourcesToReindex dbo.BulkReindexResourceTableType_1 READONLY,
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY,
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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Text, searchIndex.TextOverflow, 0, searchIndex.IsMin, searchIndex.IsMax
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
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.StartDateTime, searchIndex.EndDateTime, searchIndex.IsLongerThanADay, 0, searchIndex.IsMin, searchIndex.IsMax
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
