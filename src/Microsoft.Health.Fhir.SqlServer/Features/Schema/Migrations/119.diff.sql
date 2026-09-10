CREATE NONCLUSTERED INDEX IX_VectorSearchParam_SourceResource
ON dbo.VectorSearchParam
(
    SourceResourceTypeId,
    SourceResourceId
)
INCLUDE
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE SourceResourceTypeId IS NOT NULL AND SourceResourceId IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
GO

ALTER PROCEDURE dbo.HardDeleteResource
   @ResourceTypeId smallint
  ,@ResourceId varchar(64)
  ,@KeepCurrentVersion bit
  ,@IsResourceChangeCaptureEnabled bit
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'RT='+convert(varchar,@ResourceTypeId)+' R='+@ResourceId+' V='+convert(varchar,@KeepCurrentVersion)+' CC='+convert(varchar,@IsResourceChangeCaptureEnabled)
       ,@st datetime = getUTCdate()
       ,@InitialTranCount int = @@trancount
       ,@TransactionId bigint

BEGIN TRY
  IF @IsResourceChangeCaptureEnabled = 1 EXECUTE dbo.MergeResourcesBeginTransaction @Count = 1, @TransactionId = @TransactionId OUT

  IF @KeepCurrentVersion = 0 AND @InitialTranCount = 0
    BEGIN TRANSACTION

  DECLARE @SurrogateIds TABLE (ResourceSurrogateId BIGINT NOT NULL)

  IF @IsResourceChangeCaptureEnabled = 1 AND NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'InvisibleHistory.IsEnabled' AND Number = 0)
    UPDATE dbo.Resource
      SET IsDeleted = 1
         ,RawResource = 0xF
         ,SearchParamHash = NULL
         ,HistoryTransactionId = @TransactionId
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF
  ELSE
    DELETE dbo.Resource
      OUTPUT deleted.ResourceSurrogateId INTO @SurrogateIds
      WHERE ResourceTypeId = @ResourceTypeId
        AND ResourceId = @ResourceId
        AND (@KeepCurrentVersion = 0 OR IsHistory = 1)
        AND RawResource <> 0xF

  DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.VectorSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)

  IF @KeepCurrentVersion = 0
  BEGIN
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ResourceWriteClaim B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ReferenceSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenText B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.StringSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.UriSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.NumberSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.QuantitySearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.DateTimeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.ReferenceTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenTokenCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenDateTimeCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenQuantityCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenStringCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
    DELETE FROM B FROM @SurrogateIds A INNER LOOP JOIN dbo.TokenNumberNumberCompositeSearchParam B WITH (INDEX = 1, FORCESEEK, PAGLOCK) ON B.ResourceTypeId = @ResourceTypeId AND B.ResourceSurrogateId = A.ResourceSurrogateId OPTION (MAXDOP 1)
  END

  IF @InitialTranCount = 0 AND @@trancount > 0 COMMIT TRANSACTION

  IF @IsResourceChangeCaptureEnabled = 1 EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

CREATE PROCEDURE dbo.GetVectorSearchSourceDependencies
    @SourceResourceTypeId smallint,
    @SourceResourceId varchar(64)
AS
SET NOCOUNT ON

SELECT DISTINCT V.ResourceTypeId, R.ResourceId
FROM dbo.VectorSearchParam V
JOIN dbo.Resource R
  ON R.ResourceTypeId = V.ResourceTypeId
 AND R.ResourceSurrogateId = V.ResourceSurrogateId
 AND R.IsHistory = 0
 AND R.IsDeleted = 0
WHERE V.SourceResourceTypeId = @SourceResourceTypeId
  AND V.SourceResourceId = @SourceResourceId
  AND (V.ResourceTypeId <> @SourceResourceTypeId OR R.ResourceId <> @SourceResourceId)
GO

CREATE PROCEDURE dbo.EnqueueVectorSearchSourceRefreshJobs
    @Resources dbo.ResourceList READONLY
AS
SET NOCOUNT ON

DECLARE @Definitions dbo.StringList

INSERT INTO @Definitions (String)
SELECT DISTINCT
       (
           SELECT 11 AS TypeId,
                  RT.Name AS SourceResourceType,
                  A.ResourceId AS SourceResourceId,
                  CONVERT(varchar(64), A.Version) AS SourceResourceVersion
           FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
       )
FROM @Resources A
JOIN dbo.ResourceType RT ON RT.ResourceTypeId = A.ResourceTypeId
WHERE A.IsHistory = 0
  AND EXISTS
      (
          SELECT 1
          FROM dbo.VectorSearchParam V
          JOIN dbo.Resource R
            ON R.ResourceTypeId = V.ResourceTypeId
           AND R.ResourceSurrogateId = V.ResourceSurrogateId
           AND R.IsHistory = 0
           AND R.IsDeleted = 0
          WHERE V.SourceResourceTypeId = A.ResourceTypeId
            AND V.SourceResourceId = A.ResourceId
            AND (V.ResourceTypeId <> A.ResourceTypeId OR R.ResourceId <> A.ResourceId)
      )

IF EXISTS (SELECT 1 FROM @Definitions)
    EXECUTE dbo.EnqueueJobs
        @QueueType = 6,
        @Definitions = @Definitions,
        @ForceOneActiveJobGroup = 0,
        @ReturnJobs = 0
GO

DECLARE @MergeResourcesDefinition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.MergeResources'))
DECLARE @SingleTransactionPosition int = CHARINDEX('@SingleTransaction', @MergeResourcesDefinition)
DECLARE @SingleTransactionParameterEnd int = CHARINDEX(',', @MergeResourcesDefinition, @SingleTransactionPosition)

IF @MergeResourcesDefinition IS NULL OR @SingleTransactionPosition = 0 OR @SingleTransactionParameterEnd = 0
  THROW 50000, 'Unable to add vector source refresh support to dbo.MergeResources.', 1

SET @MergeResourcesDefinition = STUFF(
  @MergeResourcesDefinition,
  @SingleTransactionParameterEnd + 1,
  0,
  ' @EnqueueVectorSearchSourceRefresh bit = 0,')

DECLARE @CommitTransactionCallPosition int = CHARINDEX('EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId', @MergeResourcesDefinition)
DECLARE @MergeResourcesCommitPosition int = CHARINDEX('IF @InitialTranCount', @MergeResourcesDefinition, @CommitTransactionCallPosition)

IF @CommitTransactionCallPosition = 0 OR @MergeResourcesCommitPosition = 0
  THROW 50000, 'Unable to locate the commit boundary in dbo.MergeResources.', 1

SET @MergeResourcesDefinition = STUFF(
  @MergeResourcesDefinition,
  @MergeResourcesCommitPosition,
  0,
  'IF @EnqueueVectorSearchSourceRefresh = 1
  EXECUTE dbo.EnqueueVectorSearchSourceRefreshJobs @Resources = @Resources

  ')

DECLARE @MergeResourcesCreatePosition int = CHARINDEX('CREATE PROCEDURE', @MergeResourcesDefinition)

IF @MergeResourcesCreatePosition > 0
  SET @MergeResourcesDefinition = STUFF(
    @MergeResourcesDefinition,
    @MergeResourcesCreatePosition,
    LEN('CREATE PROCEDURE'),
    'ALTER PROCEDURE')
ELSE IF CHARINDEX('ALTER PROCEDURE', @MergeResourcesDefinition) = 0
  THROW 50000, 'Unable to alter dbo.MergeResources because its definition has an unexpected form.', 1

EXECUTE sp_executesql @MergeResourcesDefinition
GO

DECLARE @MergeResourcesAndSearchParamsDefinition nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.MergeResourcesAndSearchParams'))
DECLARE @TransactionIdPosition int = CHARINDEX('@TransactionId', @MergeResourcesAndSearchParamsDefinition)
DECLARE @TransactionIdParameterEnd int = CHARINDEX(',', @MergeResourcesAndSearchParamsDefinition, @TransactionIdPosition)

IF @MergeResourcesAndSearchParamsDefinition IS NULL OR @TransactionIdPosition = 0 OR @TransactionIdParameterEnd = 0
  THROW 50000, 'Unable to add vector source refresh support to dbo.MergeResourcesAndSearchParams.', 1

SET @MergeResourcesAndSearchParamsDefinition = STUFF(
  @MergeResourcesAndSearchParamsDefinition,
  @TransactionIdParameterEnd + 1,
  0,
  ' @EnqueueVectorSearchSourceRefresh bit = 0,')

DECLARE @MergeResourcesAndSearchParamsCommitPosition int = CHARINDEX('COMMIT TRANSACTION', @MergeResourcesAndSearchParamsDefinition)

IF @MergeResourcesAndSearchParamsCommitPosition = 0
  THROW 50000, 'Unable to locate the commit boundary in dbo.MergeResourcesAndSearchParams.', 1

SET @MergeResourcesAndSearchParamsDefinition = STUFF(
  @MergeResourcesAndSearchParamsDefinition,
  @MergeResourcesAndSearchParamsCommitPosition,
  0,
  'IF @EnqueueVectorSearchSourceRefresh = 1
  EXECUTE dbo.EnqueueVectorSearchSourceRefreshJobs @Resources = @Resources

  ')

DECLARE @MergeResourcesAndSearchParamsCreatePosition int = CHARINDEX('CREATE PROCEDURE', @MergeResourcesAndSearchParamsDefinition)

IF @MergeResourcesAndSearchParamsCreatePosition > 0
  SET @MergeResourcesAndSearchParamsDefinition = STUFF(
    @MergeResourcesAndSearchParamsDefinition,
    @MergeResourcesAndSearchParamsCreatePosition,
    LEN('CREATE PROCEDURE'),
    'ALTER PROCEDURE')
ELSE IF CHARINDEX('ALTER PROCEDURE', @MergeResourcesAndSearchParamsDefinition) = 0
  THROW 50000, 'Unable to alter dbo.MergeResourcesAndSearchParams because its definition has an unexpected form.', 1

EXECUTE sp_executesql @MergeResourcesAndSearchParamsDefinition
GO

CREATE PROCEDURE dbo.MergeResourcesWithVectorSearchSourceRefresh
    @AffectedRows int = 0 OUT,
    @RaiseExceptionOnConflict bit = 1,
    @IsResourceChangeCaptureEnabled bit = 0,
    @TransactionId bigint = NULL,
    @SingleTransaction bit = 1,
    @Resources dbo.ResourceList READONLY,
    @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY,
    @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY,
    @TokenSearchParams dbo.TokenSearchParamList READONLY,
    @TokenTexts dbo.TokenTextList READONLY,
    @StringSearchParams dbo.StringSearchParamList READONLY,
    @UriSearchParams dbo.UriSearchParamList READONLY,
    @NumberSearchParams dbo.NumberSearchParamList READONLY,
    @QuantitySearchParams dbo.QuantitySearchParamList READONLY,
    @DateTimeSearchParms dbo.DateTimeSearchParamList READONLY,
    @VectorSearchParams dbo.VectorSearchParamList READONLY,
    @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY,
    @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY,
    @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY,
    @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY,
    @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY,
    @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
SET NOCOUNT ON
EXECUTE dbo.MergeResources
    @AffectedRows = @AffectedRows OUT,
    @RaiseExceptionOnConflict = @RaiseExceptionOnConflict,
    @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled,
    @TransactionId = @TransactionId,
    @SingleTransaction = @SingleTransaction,
    @EnqueueVectorSearchSourceRefresh = 1,
    @Resources = @Resources,
    @ResourceWriteClaims = @ResourceWriteClaims,
    @ReferenceSearchParams = @ReferenceSearchParams,
    @TokenSearchParams = @TokenSearchParams,
    @TokenTexts = @TokenTexts,
    @StringSearchParams = @StringSearchParams,
    @UriSearchParams = @UriSearchParams,
    @NumberSearchParams = @NumberSearchParams,
    @QuantitySearchParams = @QuantitySearchParams,
    @DateTimeSearchParms = @DateTimeSearchParms,
    @VectorSearchParams = @VectorSearchParams,
    @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
    @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
    @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
    @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
    @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
    @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams
GO

CREATE PROCEDURE dbo.MergeResourcesAndSearchParamsWithVectorSearchSourceRefresh
    @SearchParams dbo.SearchParamList READONLY,
    @ReindexId bigint = NULL,
    @IsResourceChangeCaptureEnabled bit = 0,
    @TransactionId bigint = NULL,
    @Resources dbo.ResourceList READONLY,
    @ResourceWriteClaims dbo.ResourceWriteClaimList READONLY,
    @ReferenceSearchParams dbo.ReferenceSearchParamList READONLY,
    @TokenSearchParams dbo.TokenSearchParamList READONLY,
    @TokenTexts dbo.TokenTextList READONLY,
    @StringSearchParams dbo.StringSearchParamList READONLY,
    @UriSearchParams dbo.UriSearchParamList READONLY,
    @NumberSearchParams dbo.NumberSearchParamList READONLY,
    @QuantitySearchParams dbo.QuantitySearchParamList READONLY,
    @DateTimeSearchParms dbo.DateTimeSearchParamList READONLY,
    @VectorSearchParams dbo.VectorSearchParamList READONLY,
    @ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY,
    @TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY,
    @TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY,
    @TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY,
    @TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY,
    @TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
SET NOCOUNT ON
EXECUTE dbo.MergeResourcesAndSearchParams
    @SearchParams = @SearchParams,
    @ReindexId = @ReindexId,
    @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled,
    @TransactionId = @TransactionId,
    @EnqueueVectorSearchSourceRefresh = 1,
    @Resources = @Resources,
    @ResourceWriteClaims = @ResourceWriteClaims,
    @ReferenceSearchParams = @ReferenceSearchParams,
    @TokenSearchParams = @TokenSearchParams,
    @TokenTexts = @TokenTexts,
    @StringSearchParams = @StringSearchParams,
    @UriSearchParams = @UriSearchParams,
    @NumberSearchParams = @NumberSearchParams,
    @QuantitySearchParams = @QuantitySearchParams,
    @DateTimeSearchParms = @DateTimeSearchParms,
    @VectorSearchParams = @VectorSearchParams,
    @ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams,
    @TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams,
    @TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams,
    @TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams,
    @TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams,
    @TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams
GO

CREATE PROCEDURE dbo.HardDeleteResourceWithVectorSearchSourceRefresh
    @ResourceTypeId smallint,
    @ResourceId varchar(64),
    @KeepCurrentVersion bit,
    @IsResourceChangeCaptureEnabled bit
AS
SET NOCOUNT ON
DECLARE @InitialTranCount int = @@TRANCOUNT
DECLARE @ChangedSources dbo.ResourceList

BEGIN TRY
  IF @InitialTranCount = 0 BEGIN TRANSACTION

  IF @KeepCurrentVersion = 0
    INSERT INTO @ChangedSources
        (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare, IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod, SearchParamHash)
    SELECT TOP (1)
        ResourceTypeId, ResourceSurrogateId, ResourceId, Version + 1, 0, 1, 0, 0, 0x0, 0, NULL, NULL
    FROM dbo.Resource
    WHERE ResourceTypeId = @ResourceTypeId
      AND ResourceId = @ResourceId
      AND IsHistory = 0
    ORDER BY ResourceSurrogateId DESC

  EXECUTE dbo.HardDeleteResource
      @ResourceTypeId = @ResourceTypeId,
      @ResourceId = @ResourceId,
      @KeepCurrentVersion = @KeepCurrentVersion,
      @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled

  EXECUTE dbo.EnqueueVectorSearchSourceRefreshJobs @Resources = @ChangedSources

  IF @InitialTranCount = 0 COMMIT TRANSACTION
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION
  THROW
END CATCH
GO
