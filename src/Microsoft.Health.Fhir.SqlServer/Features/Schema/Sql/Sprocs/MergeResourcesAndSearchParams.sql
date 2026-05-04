CREATE PROCEDURE dbo.MergeResourcesAndSearchParams 
     @SearchParams dbo.SearchParamList READONLY
    ,@ReindexId bigint = -1
    ,@IsResourceChangeCaptureEnabled bit = 0
    ,@TransactionId bigint = NULL
    ,@Resources dbo.ResourceList READONLY
    ,@ResourceWriteClaims dbo.ResourceWriteClaimList READONLY
    ,@ReferenceSearchParams dbo.ReferenceSearchParamList READONLY
    ,@TokenSearchParams dbo.TokenSearchParamList READONLY
    ,@TokenTexts dbo.TokenTextList READONLY
    ,@StringSearchParams dbo.StringSearchParamList READONLY
    ,@UriSearchParams dbo.UriSearchParamList READONLY
    ,@NumberSearchParams dbo.NumberSearchParamList READONLY
    ,@QuantitySearchParams dbo.QuantitySearchParamList READONLY
    ,@DateTimeSearchParms dbo.DateTimeSearchParamList READONLY
    ,@ReferenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamList READONLY
    ,@TokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamList READONLY
    ,@TokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamList READONLY
    ,@TokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamList READONLY
    ,@TokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamList READONLY
    ,@TokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamList READONLY
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'R='+convert(varchar,(SELECT count(*) FROM @Resources))+' SP='+convert(varchar,(SELECT count(*) FROM @SearchParams))
       ,@st datetime = getUTCdate()
       ,@Rows int = 0

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

  BEGIN TRANSACTION
  
  EXECUTE dbo.MergeSearchParams @SearchParams, @ReindexId

  IF EXISTS (SELECT * FROM @Resources)
    EXECUTE dbo.MergeResources
             @AffectedRows = @Rows OUTPUT
            ,@RaiseExceptionOnConflict = 1
            ,@IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled
            ,@TransactionId = @TransactionId
            ,@SingleTransaction = 1
            ,@Resources = @Resources
            ,@ResourceWriteClaims = @ResourceWriteClaims
            ,@ReferenceSearchParams = @ReferenceSearchParams
            ,@TokenSearchParams = @TokenSearchParams
            ,@TokenTexts = @TokenTexts
            ,@StringSearchParams = @StringSearchParams
            ,@UriSearchParams = @UriSearchParams
            ,@NumberSearchParams = @NumberSearchParams
            ,@QuantitySearchParams = @QuantitySearchParams
            ,@DateTimeSearchParms = @DateTimeSearchParms
            ,@ReferenceTokenCompositeSearchParams = @ReferenceTokenCompositeSearchParams
            ,@TokenTokenCompositeSearchParams = @TokenTokenCompositeSearchParams
            ,@TokenDateTimeCompositeSearchParams = @TokenDateTimeCompositeSearchParams
            ,@TokenQuantityCompositeSearchParams = @TokenQuantityCompositeSearchParams
            ,@TokenStringCompositeSearchParams = @TokenStringCompositeSearchParams
            ,@TokenNumberNumberCompositeSearchParams = @TokenNumberNumberCompositeSearchParams;

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO Parameters (Id,Char) SELECT 'MergeResourcesAndSearchParams','LogEvent'
GO
--DECLARE @SearchParams dbo.SearchParamList
--INSERT INTO @SearchParams
--  --SELECT 'http://example.org/fhir/SearchParameter/custom-mixed-base-d9e18fc8', 'Enabled', 0, '2026-01-26 17:15:43.0364438 -08:00'
--  SELECT 'Test', 'Enabled', 0, '2026-01-26 17:15:43.0364438 -08:00'
--INSERT INTO @SearchParams
--  SELECT 'Test2', 'Enabled', 0, '2026-01-26 17:15:43.0364438 -08:00'
--SELECT * FROM @SearchParams
--EXECUTE dbo.MergeResourcesAndSearchParams @SearchParams
--SELECT TOP 100 * FROM SearchParam ORDER BY SearchParamId DESC
--DELETE FROM SearchParam WHERE Uri LIKE 'Test%'
--SELECT TOP 10 * FROM EventLog ORDER BY EventDate DESC
