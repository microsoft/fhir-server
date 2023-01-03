
/*************************************************************
    Stored procedure for creating and deleting
**************************************************************/
--
-- STORED PROCEDURE
--     UpsertResource_7
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
--     @requireETagOnUpdate
--         * True if this is a versioned update and an etag must be provided
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
--     @comparedVersion
--         *  If specified, the version of the resource that was compared in the code
--
-- RETURN VALUE
--     The version of the resource as a result set. Will be empty if no insertion was done.
GO
CREATE PROCEDURE dbo.UpsertResource_7
    @baseResourceSurrogateId bigint,
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @keepHistory bit,
    @requireETagOnUpdate bit,
    @requestMethod varchar(10),
    @searchParamHash varchar(64),
    @rawResource varbinary(max),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_2 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 READONLY,
    @isResourceChangeCaptureEnabled bit = 0,
    @comparedVersion int = NULL
AS
set nocount on
set xact_abort on

-- variables for the existing version of the resource that will be replaced
DECLARE @previousResourceSurrogateId bigint
       ,@previousVersion bigint
       ,@previousIsDeleted bit
       ,@version int -- the version of the resource being written
       ,@resourceSurrogateId bigint
       ,@InitialTranCount int = @@trancount

IF @InitialTranCount = 0 BEGIN TRANSACTION

SELECT @previousResourceSurrogateId = ResourceSurrogateId
      ,@previousVersion = Version
      ,@previousIsDeleted = IsDeleted
  FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK) -- This should place a range lock on a row in the IX_Resource_ResourceTypeId_ResourceId nonclustered filtered index
  WHERE ResourceTypeId = @resourceTypeId
    AND ResourceId = @resourceId
    AND IsHistory = 0

IF @previousResourceSurrogateId IS NULL -- There is no previous version
  SET @version = 1
ELSE
BEGIN -- There is a previous version so @previousVersion will not be null
  IF @isDeleted = 0 -- When not a delete
  BEGIN
    IF @comparedVersion IS NULL OR @comparedVersion <> @previousVersion
    BEGIN
      -- If @comparedVersion is null then resource was recently added
      -- Otherwise if @comparedVersion doesn't match the @previousVersion in the DB means the version we compared in the code is not the latest version anymore
      -- Go back to code and compare the latest
      THROW 50409, 'Resource has been recently updated or added, please compare the resource content in code for any duplicate updates', 1
    END
  END

  SET @version = @previousVersion + 1
  IF @keepHistory = 1
    UPDATE dbo.Resource SET IsHistory = 1 WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId -- Set the existing resource as history
  ELSE
  BEGIN 
    DELETE dbo.Resource WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
    DELETE dbo.ResourceWriteClaim WHERE ResourceSurrogateId = @previousResourceSurrogateId
  END

  -- delete history from index tables because we do not search on them
  DELETE dbo.CompartmentAssignment WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.ReferenceSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenText WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.StringSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.UriSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.NumberSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.QuantitySearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.DateTimeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.ReferenceTokenCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenTokenCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenDateTimeCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenQuantityCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenStringCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
  DELETE dbo.TokenNumberNumberCompositeSearchParam WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId
END

SET @resourceSurrogateId = @baseResourceSurrogateId + (NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence)

INSERT INTO dbo.Resource (ResourceTypeId,  ResourceId,  Version, IsHistory,  ResourceSurrogateId,  IsDeleted,  RequestMethod,  RawResource,                     IsRawResourceMetaSet,  SearchParamHash)
  SELECT                 @resourceTypeId, @resourceId, @version,         0, @resourceSurrogateId, @isDeleted, @requestMethod, @rawResource, CASE WHEN @version = 1 THEN 1 ELSE 0 END, @searchParamHash

INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
  SELECT                           @resourceSurrogateId, ClaimTypeId, ClaimValue
    FROM @resourceWriteClaims

INSERT INTO dbo.CompartmentAssignment (ResourceTypeId,  ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
  SELECT DISTINCT                     @resourceTypeId, @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId,         0
    FROM @compartmentAssignments

INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
  SELECT DISTINCT                    @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion,         0
    FROM @referenceSearchParams

INSERT INTO dbo.TokenSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
  SELECT DISTINCT                @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow,         0
    FROM @tokenSearchParams

INSERT INTO dbo.TokenText (ResourceTypeId,  ResourceSurrogateId, SearchParamId, Text, IsHistory)
  SELECT DISTINCT         @resourceTypeId, @resourceSurrogateId, SearchParamId, Text,         0
    FROM @tokenTextSearchParams

INSERT INTO dbo.StringSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
  SELECT DISTINCT                 @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow,         0, IsMin, IsMax
    FROM @stringSearchParams

INSERT INTO dbo.UriSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, Uri, IsHistory)
  SELECT DISTINCT              @resourceTypeId, @resourceSurrogateId, SearchParamId, Uri,         0
    FROM @uriSearchParams

INSERT INTO dbo.NumberSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
  SELECT DISTINCT                 @resourceTypeId, @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue,         0
    FROM @numberSearchParams

INSERT INTO dbo.QuantitySearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
  SELECT DISTINCT                   @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue,         0
    FROM @quantitySearchParams

INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
  SELECT DISTINCT                   @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay,         0, IsMin, IsMax
    FROM @dateTimeSearchParms

INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
  SELECT DISTINCT                                  @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2,         0
    FROM @referenceTokenCompositeSearchParams

INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
  SELECT DISTINCT                              @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2,         0
    FROM @tokenTokenCompositeSearchParams

INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
  SELECT DISTINCT                                 @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2,         0
    FROM @tokenDateTimeCompositeSearchParams

INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
  SELECT DISTINCT                                 @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2,         0
    FROM @tokenQuantityCompositeSearchParams

INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
  SELECT DISTINCT                               @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2,         0
    FROM @tokenStringCompositeSearchParams

INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId,  ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
  SELECT DISTINCT                                     @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange,         0
    FROM @tokenNumberNumberCompositeSearchParams

SELECT @version

IF @isResourceChangeCaptureEnabled = 1 --If the resource change capture feature is enabled, to execute a stored procedure called CaptureResourceChanges to insert resource change data.
  EXECUTE dbo.CaptureResourceChanges @isDeleted = @isDeleted, @version = @version, @resourceId = @resourceId, @resourceTypeId = @resourceTypeId

IF @InitialTranCount = 0 COMMIT TRANSACTION

GO
