CREATE PROCEDURE dbo.UpdateResourceSearchParamsWithVectors
    @FailedResources int = 0 OUT
   ,@Resources dbo.ResourceList READONLY
   ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
   ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
   ,@TokenSearchParams dbo.TokenSearchParamList READONLY
   ,@TokenTexts dbo.TokenTextList READONLY
   ,@StringSearchParams dbo.StringSearchParamList READONLY
   ,@UriSearchParams dbo.UriSearchParamList READONLY
   ,@NumberSearchParams dbo.NumberSearchParamList READONLY
   ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
   ,@DateTimeSearchParams dbo.DateTimeSearchParamList READONLY
   ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
   ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
   ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
   ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
   ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
   ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
   ,@VectorSearchResources dbo.ResourceList READONLY
   ,@VectorSearchParams dbo.VectorSearchParamList READONLY
AS
SET NOCOUNT ON
DECLARE @InitialTranCount int = @@TRANCOUNT

BEGIN TRY
  IF @InitialTranCount = 0 BEGIN TRANSACTION

  EXECUTE dbo.UpdateResourceSearchParams
     @FailedResources = @FailedResources OUT
    ,@Resources = @Resources
    ,@ResourceWriteClaims = @ResourceWriteClaims
    ,@ReferenceSearchParams = @ReferenceSearchParams
    ,@TokenSearchParams = @TokenSearchParams
    ,@TokenTexts = @TokenTexts
    ,@StringSearchParams = @StringSearchParams
    ,@UriSearchParams = @UriSearchParams
    ,@NumberSearchParams = @NumberSearchParams
    ,@QuantitySearchParams = @QuantitySearchParams
    ,@DateTimeSearchParams = @DateTimeSearchParams
    ,@ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams
    ,@TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams
    ,@TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams
    ,@TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams
    ,@TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams
    ,@TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams

  DECLARE @Ids TABLE
  (
      ResourceTypeId smallint NOT NULL,
      ResourceSurrogateId bigint NOT NULL,
      PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
  )

  INSERT INTO @Ids
         ( ResourceTypeId, ResourceSurrogateId )
    SELECT A.ResourceTypeId, A.ResourceSurrogateId
      FROM @VectorSearchResources A
           JOIN dbo.Resource B WITH (UPDLOCK, HOLDLOCK)
             ON B.ResourceTypeId = A.ResourceTypeId
            AND B.ResourceSurrogateId = A.ResourceSurrogateId
            AND B.ResourceId = A.ResourceId
            AND B.Version = A.Version
     WHERE B.IsHistory = 0

  DELETE V
    FROM dbo.VectorSearchParam V
         JOIN @Ids I
           ON I.ResourceTypeId = V.ResourceTypeId
          AND I.ResourceSurrogateId = V.ResourceSurrogateId

  INSERT INTO dbo.VectorSearchParam
         ( ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, ChunkText, SourceTextHash, SourceResourceTypeId, SourceResourceId, SourceResourceVersion, SourcePath, Embedding )
    SELECT V.ResourceTypeId, V.ResourceSurrogateId, V.SearchParamId, V.ChunkOrdinal, V.EmbeddingModelId, V.ChunkText, V.SourceTextHash, V.SourceResourceTypeId, V.SourceResourceId, V.SourceResourceVersion, V.SourcePath, CAST(V.Embedding AS vector(1536))
      FROM @VectorSearchParams V
           JOIN @Ids I
             ON I.ResourceTypeId = V.ResourceTypeId
            AND I.ResourceSurrogateId = V.ResourceSurrogateId

  IF @InitialTranCount = 0 COMMIT TRANSACTION
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION
  THROW
END CATCH
GO