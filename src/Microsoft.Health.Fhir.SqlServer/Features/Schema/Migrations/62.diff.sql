IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'BulkNumberSearchParamTableType_2')
CREATE TYPE dbo.BulkNumberSearchParamTableType_2 AS TABLE (
    Offset        INT             NOT NULL,
    SearchParamId SMALLINT        NOT NULL,
    SingleValue   DECIMAL (36, 18) NULL,
    LowValue      DECIMAL (36, 18) NULL,
    HighValue     DECIMAL (36, 18) NULL);

IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'BulkQuantitySearchParamTableType_2')
CREATE TYPE dbo.BulkQuantitySearchParamTableType_2 AS TABLE (
    Offset         INT             NOT NULL,
    SearchParamId  SMALLINT        NOT NULL,
    SystemId       INT             NULL,
    QuantityCodeId INT             NULL,
    SingleValue    DECIMAL (36, 18) NULL,
    LowValue       DECIMAL (36, 18) NULL,
    HighValue      DECIMAL (36, 18) NULL);


IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'BulkTokenQuantityCompositeSearchParamTableType_3')
CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_3 AS TABLE (
    Offset          INT             NOT NULL,
    SearchParamId   SMALLINT        NOT NULL,
    SystemId1       INT             NULL,
    Code1           VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1   VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2       INT             NULL,
    QuantityCodeId2 INT             NULL,
    SingleValue2    DECIMAL (36, 18) NULL,
    LowValue2       DECIMAL (36, 18) NULL,
    HighValue2      DECIMAL (36, 18) NULL);

IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'BulkTokenNumberNumberCompositeSearchParamTableType_3')
CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_3 AS TABLE (
    Offset        INT             NOT NULL,
    SearchParamId SMALLINT        NOT NULL,
    SystemId1     INT             NULL,
    Code1         VARCHAR (256)   COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 VARCHAR (MAX)   COLLATE Latin1_General_100_CS_AS NULL,
    SingleValue2  DECIMAL (36, 18) NULL,
    LowValue2     DECIMAL (36, 18) NULL,
    HighValue2    DECIMAL (36, 18) NULL,
    SingleValue3  DECIMAL (36, 18) NULL,
    LowValue3     DECIMAL (36, 18) NULL,
    HighValue3    DECIMAL (36, 18) NULL,
    HasRange      BIT             NOT NULL);

GO
ALTER PROCEDURE dbo.BulkReindexResources_2
@resourcesToReindex dbo.BulkReindexResourceTableType_1 READONLY, @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_2 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_2 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_3 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_3 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @computedValues TABLE (
    Offset              INT          NOT NULL,
    ResourceTypeId      SMALLINT     NOT NULL,
    VersionProvided     BIGINT       NULL,
    SearchParamHash     VARCHAR (64) NOT NULL,
    ResourceSurrogateId BIGINT       NULL,
    VersionInDatabase   BIGINT       NULL);
INSERT INTO @computedValues
SELECT resourceToReindex.Offset,
       resourceToReindex.ResourceTypeId,
       resourceToReindex.ETag,
       resourceToReindex.SearchParamHash,
       resourceInDB.ResourceSurrogateId,
       resourceInDB.Version
FROM   @resourcesToReindex AS resourceToReindex
       LEFT OUTER JOIN
       dbo.Resource AS resourceInDB WITH (UPDLOCK, INDEX (IX_Resource_ResourceTypeId_ResourceId))
       ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND resourceInDB.ResourceId = resourceToReindex.ResourceId
          AND resourceInDB.IsHistory = 0;
DECLARE @versionDiff AS INT;
SET @versionDiff = (SELECT COUNT(*)
                    FROM   @computedValues
                    WHERE  VersionProvided IS NOT NULL
                           AND VersionProvided <> VersionInDatabase);
IF (@versionDiff > 0)
    BEGIN
        DELETE @computedValues
        WHERE  VersionProvided IS NOT NULL
               AND VersionProvided <> VersionInDatabase;
    END
UPDATE resourceInDB
SET    resourceInDB.SearchParamHash = resourceToReindex.SearchParamHash
FROM   @computedValues AS resourceToReindex
       INNER JOIN
       dbo.Resource AS resourceInDB
       ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND resourceInDB.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ResourceWriteClaim AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.CompartmentAssignment AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ReferenceSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenText AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.StringSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.UriSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.NumberSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.QuantitySearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.DateTimeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ReferenceTokenCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenTokenCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenDateTimeCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenQuantityCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenStringCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenNumberNumberCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT DISTINCT resourceToReindex.ResourceSurrogateId,
                searchIndex.ClaimTypeId,
                searchIndex.ClaimValue
FROM   @resourceWriteClaims AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.CompartmentTypeId,
                searchIndex.ReferenceResourceId,
                0
FROM   @compartmentAssignments AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.BaseUri,
                searchIndex.ReferenceResourceTypeId,
                searchIndex.ReferenceResourceId,
                searchIndex.ReferenceResourceVersion,
                0
FROM   @referenceSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId,
                searchIndex.Code,
                searchIndex.CodeOverflow,
                0
FROM   @tokenSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Text,
                0
FROM   @tokenTextSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Text,
                searchIndex.TextOverflow,
                0,
                searchIndex.IsMin,
                searchIndex.IsMax
FROM   @stringSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Uri,
                0
FROM   @uriSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SingleValue,
                searchIndex.LowValue,
                searchIndex.HighValue,
                0
FROM   @numberSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId,
                searchIndex.QuantityCodeId,
                searchIndex.SingleValue,
                searchIndex.LowValue,
                searchIndex.HighValue,
                0
FROM   @quantitySearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.StartDateTime,
                searchIndex.EndDateTime,
                searchIndex.IsLongerThanADay,
                0,
                searchIndex.IsMin,
                searchIndex.IsMax
FROM   @dateTimeSearchParms AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.BaseUri1,
                searchIndex.ReferenceResourceTypeId1,
                searchIndex.ReferenceResourceId1,
                searchIndex.ReferenceResourceVersion1,
                searchIndex.SystemId2,
                searchIndex.Code2,
                searchIndex.CodeOverflow2,
                0
FROM   @referenceTokenCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.SystemId2,
                searchIndex.Code2,
                searchIndex.CodeOverflow2,
                0
FROM   @tokenTokenCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.StartDateTime2,
                searchIndex.EndDateTime2,
                searchIndex.IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.SingleValue2,
                searchIndex.SystemId2,
                searchIndex.QuantityCodeId2,
                searchIndex.LowValue2,
                searchIndex.HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.Text2,
                searchIndex.TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.CodeOverflow1,
                searchIndex.SingleValue2,
                searchIndex.LowValue2,
                searchIndex.HighValue2,
                searchIndex.SingleValue3,
                searchIndex.LowValue3,
                searchIndex.HighValue3,
                searchIndex.HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
SELECT @versionDiff;
COMMIT TRANSACTION;

GO
ALTER PROCEDURE dbo.ReindexResource_2
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @eTag INT=NULL, @searchParamHash VARCHAR (64), @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_2 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_2 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_2 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_2 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_2 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_2 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_3 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_2 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_3 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resourceSurrogateId AS BIGINT;
DECLARE @version AS BIGINT;
SELECT @resourceSurrogateId = ResourceSurrogateId,
       @version = Version
FROM   dbo.Resource WITH (UPDLOCK, HOLDLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND IsHistory = 0;
IF (@etag IS NOT NULL
    AND @etag <> @version)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
UPDATE dbo.Resource
SET    SearchParamHash = @searchParamHash
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ResourceWriteClaim
WHERE  ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.CompartmentAssignment
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ReferenceSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenText
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.StringSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.UriSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.NumberSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.QuantitySearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.DateTimeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ReferenceTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenDateTimeCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenQuantityCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenStringCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenNumberNumberCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT @resourceSurrogateId,
       ClaimTypeId,
       ClaimValue
FROM   @resourceWriteClaims;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                CompartmentTypeId,
                ReferenceResourceId,
                0
FROM   @compartmentAssignments;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri,
                ReferenceResourceTypeId,
                ReferenceResourceId,
                ReferenceResourceVersion,
                0
FROM   @referenceSearchParams;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, CodeOverflow, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                Code,
                CodeOverflow,
                0
FROM   @tokenSearchParams;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                0
FROM   @tokenTextSearchParams;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                TextOverflow,
                0,
                IsMin,
                IsMax
FROM   @stringSearchParams;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Uri,
                0
FROM   @uriSearchParams;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @numberSearchParams;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                QuantityCodeId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @quantitySearchParams;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                StartDateTime,
                EndDateTime,
                IsLongerThanADay,
                0,
                IsMin,
                IsMax
FROM   @dateTimeSearchParms;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri1,
                ReferenceResourceTypeId1,
                ReferenceResourceId1,
                ReferenceResourceVersion1,
                SystemId2,
                Code2,
                CodeOverflow2,
                0
FROM   @referenceTokenCompositeSearchParams;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SystemId2, Code2, CodeOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SystemId2,
                Code2,
                CodeOverflow2,
                0
FROM   @tokenTokenCompositeSearchParams;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                StartDateTime2,
                EndDateTime2,
                IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SingleValue2,
                SystemId2,
                QuantityCodeId2,
                LowValue2,
                HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                Text2,
                TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, CodeOverflow1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                CodeOverflow1,
                SingleValue2,
                LowValue2,
                HighValue2,
                SingleValue3,
                LowValue3,
                HighValue3,
                HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams;
COMMIT TRANSACTION;

