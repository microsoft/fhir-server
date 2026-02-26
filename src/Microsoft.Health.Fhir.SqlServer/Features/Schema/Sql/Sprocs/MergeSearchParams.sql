CREATE PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY
           ,@IsResourceChangeCaptureEnabled bit = 0
           ,@TransactionId bigint = NULL
           ,@SingleTransaction bit = 1
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
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,(SELECT count(*) FROM @SearchParams))
       ,@st datetime = getUTCdate()
       ,@LastUpdated datetimeoffset(7) = sysdatetimeoffset()
       ,@msg varchar(4000)
       ,@Rows int
       ,@AffectedRows int = 0
       ,@Uri varchar(4000)
       ,@Status varchar(20)

DECLARE @SearchParamsCopy dbo.SearchParamList
INSERT INTO @SearchParamsCopy SELECT * FROM @SearchParams
WHILE EXISTS (SELECT * FROM @SearchParamsCopy)
BEGIN
  SELECT TOP 1 @Uri = Uri, @Status = Status FROM @SearchParamsCopy
  SET @msg = 'Uri='+@Uri+' Status='+@Status
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@msg
  DELETE FROM @SearchParamsCopy WHERE Uri = @Uri
END

DECLARE @SummaryOfChanges TABLE (Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL, Operation varchar(20) NOT NULL)

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

  BEGIN TRANSACTION
  
  -- Check for concurrency conflicts first using LastUpdated
  SELECT @msg = string_agg(S.Uri, ', ') 
    FROM @SearchParams I JOIN dbo.SearchParam S ON S.Uri = I.Uri
    WHERE I.LastUpdated != S.LastUpdated
  IF @msg IS NOT NULL
  BEGIN
    SET @msg = concat('Optimistic concurrency conflict detected for search parameters: ', @msg) 
    ROLLBACK TRANSACTION;
    THROW 50001, @msg, 1
  END

  MERGE INTO dbo.SearchParam S
    USING @SearchParams I ON I.Uri = S.Uri
    WHEN MATCHED THEN 
      UPDATE 
        SET Status = I.Status
           ,LastUpdated = @LastUpdated
           ,IsPartiallySupported = I.IsPartiallySupported
    WHEN NOT MATCHED BY TARGET THEN 
      INSERT   (  Uri,   Status,  LastUpdated,   IsPartiallySupported) 
        VALUES (I.Uri, I.Status, @LastUpdated, I.IsPartiallySupported)
    OUTPUT I.Uri, $action INTO @SummaryOfChanges;
  SET @Rows = @@rowcount

  SELECT S.SearchParamId
        ,S.Uri
        ,S.LastUpdated
    FROM dbo.SearchParam S JOIN @SummaryOfChanges C ON C.Uri = S.Uri
    WHERE C.Operation = 'INSERT'
  SET @msg = 'LastUpdated='+substring(convert(varchar,@LastUpdated),1,23)+' INSERT='+convert(varchar,@@rowcount)

  IF EXISTS (SELECT * FROM @Resources)
  BEGIN
    EXECUTE dbo.MergeResources
     @AffectedRows = @AffectedRows OUTPUT
    ,@RaiseExceptionOnConflict = 1
    ,@IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled
    ,@TransactionId = @TransactionId
    ,@SingleTransaction = @SingleTransaction
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

    SET @Rows = @Rows + @AffectedRows;
  END

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO Parameters (Id,Char) SELECT 'MergeSearchParams','LogEvent'
GO
--DECLARE @SearchParams dbo.SearchParamList
--INSERT INTO @SearchParams
--  --SELECT 'http://example.org/fhir/SearchParameter/custom-mixed-base-d9e18fc8', 'Enabled', 0, '2026-01-26 17:15:43.0364438 -08:00'
--  SELECT 'Test', 'Enabled', 0, '2026-01-26 17:15:43.0364438 -08:00'
--INSERT INTO @SearchParams
--  SELECT 'Test2', 'Enabled', 0, '2026-01-26 17:15:43.0364438 -08:00'
--SELECT * FROM @SearchParams
--EXECUTE dbo.MergeSearchParams @SearchParams
--SELECT TOP 100 * FROM SearchParam ORDER BY SearchParamId DESC
--DELETE FROM SearchParam WHERE Uri LIKE 'Test%'
--SELECT TOP 10 * FROM EventLog ORDER BY EventDate DESC
