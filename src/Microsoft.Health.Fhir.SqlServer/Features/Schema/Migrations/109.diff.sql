IF object_id('UpsertSearchParamsWithOptimisticConcurrency') IS NOT NULL DROP PROCEDURE UpsertSearchParamsWithOptimisticConcurrency
GO
ALTER PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY
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
DECLARE @InitialTranCount int = @@trancount

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

  IF @InitialTranCount = 0
  BEGIN
    BEGIN TRANSACTION
  END
  
  -- Check for concurrency conflicts first using LastUpdated
  SELECT @msg = string_agg(S.Uri, ', ') 
    FROM @SearchParams I JOIN dbo.SearchParam S ON S.Uri = I.Uri
    WHERE I.LastUpdated != S.LastUpdated
  IF @msg IS NOT NULL
  BEGIN
    SET @msg = concat('Optimistic concurrency conflict detected for search parameters: ', @msg) 
    IF @InitialTranCount = 0 ROLLBACK TRANSACTION;
    THROW 50001, @msg, 1
  END

  IF EXISTS (SELECT * FROM @Resources)
  BEGIN
    EXECUTE dbo.MergeResources
     @AffectedRows = @AffectedRows OUTPUT
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

    SET @Rows = @Rows + @AffectedRows;
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

  IF @InitialTranCount = 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION;
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO Parameters (Id,Char) SELECT 'MergeSearchParams','LogEvent' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'MergeSearchParams')
GO
-- Enable event logging for DequeueJob to allow active host discovery via EventLog.HostName
INSERT INTO dbo.Parameters (Id, Char) SELECT 'DequeueJob', 'LogEvent' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'DequeueJob')
GO
-- Enable event logging for cache refresh convergence tracking and diagnostics
INSERT INTO dbo.Parameters (Id, Char) SELECT 'SearchParameterCacheRefresh', 'LogEvent' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'SearchParameterCacheRefresh')
GO
CREATE OR ALTER PROCEDURE dbo.CheckSearchParamCacheConsistency
    @TargetSearchParamLastUpdated varchar(100)
   ,@SyncStartDate datetime2(7)
   ,@ActiveHostsSince datetime2(7)
   ,@StalenessThresholdMinutes int = 10
AS
set nocount on
SELECT HostName
    ,CAST(NULL AS datetime2(7)) AS SyncEventDate
      ,CAST(NULL AS nvarchar(3500)) AS EventText
  FROM dbo.EventLog
  WHERE EventDate >= @ActiveHostsSince
    AND HostName IS NOT NULL
    AND Process = 'DequeueJob'

UNION ALL

SELECT HostName
    ,EventDate
      ,EventText
  FROM dbo.EventLog
  WHERE EventDate >= @SyncStartDate
    AND HostName IS NOT NULL
    AND Process = 'SearchParameterCacheRefresh'
    AND Status = 'End'
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
