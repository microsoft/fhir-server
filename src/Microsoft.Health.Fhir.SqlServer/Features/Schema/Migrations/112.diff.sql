ALTER PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY, @InputLastUpdated datetimeoffset(7) = NULL, @ReindexId bigint = -1
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,(SELECT count(*) FROM @SearchParams))+' L='+isnull(convert(varchar(23),@InputLastUpdated,126),'NULL')+' R='+convert(varchar,@ReindexId)
       ,@st datetime = getUTCdate()
       ,@LastUpdated datetimeoffset(7) = convert(datetimeoffset(7), sysUTCdatetime())
       ,@MaxLastUpdated datetimeoffset(7)
       ,@msg varchar(4000)
       ,@Rows int
       ,@Uri varchar(4000)
       ,@Status varchar(20)
       ,@InputTrancount int = @@trancount
       ,@RunningReindexId bigint

DECLARE @SearchParamsCopy dbo.SearchParamList
INSERT INTO @SearchParamsCopy SELECT * FROM @SearchParams
WHILE EXISTS (SELECT * FROM @SearchParamsCopy)
BEGIN
  SELECT TOP 1 @Uri = Uri, @Status = Status FROM @SearchParamsCopy
  SET @msg = 'Status='+@Status+' Uri='+@Uri
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@msg
  DELETE FROM @SearchParamsCopy WHERE Uri = @Uri
END

BEGIN TRY
  IF @InputTrancount = 0 -- transaction can start in MergeResourcesAndSearchParams
  BEGIN 
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    BEGIN TRANSACTION
  END

  SET @RunningReindexId = (SELECT TOP 1 GroupId FROM dbo.JobQueue WHERE QueueType = 6 AND Status IN (0,1))
  IF @ReindexId <> -1 AND @RunningReindexId IS NOT NULL AND @RunningReindexId <> @ReindexId -- @ReindexId = -1 old code
  BEGIN
    SET @msg = 'Reindex job is in progress. ReindexId='+isnull(convert(varchar,@RunningReindexId),'NULL')
    ROLLBACK TRANSACTION;
    THROW 50002, @msg, 1
  END
  
  IF @InputLastUpdated IS NOT NULL -- check concurrency based on max(LastUpdated)
  BEGIN
    SET @MaxLastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)
    IF @MaxLastUpdated <> @InputLastUpdated AND @ReindexId IS NULL
    BEGIN
      SET @msg = 'Optimistic concurrency conflict detected : input last updated = '+convert(varchar(23),@InputLastUpdated,126)+' max last updated = '+convert(varchar(23),@MaxLastUpdated,126)
      ROLLBACK TRANSACTION;
      THROW 50001, @msg, 1
    END
  END
  ELSE
  BEGIN
    -- remove below block once all callers are updated to pass LastUpdated
    -- Check for concurrency conflicts first using LastUpdated
    -- Only the top 60 are included in the message to avoid hitting the 8000 character limit, but all conflicts will cause the transaction to roll back
    SELECT @msg = string_agg(S.Uri, ', ') 
      FROM (
        SELECT TOP 60 S.Uri
          FROM @SearchParams I JOIN dbo.SearchParam S ON S.Uri = I.Uri
          WHERE I.LastUpdated != S.LastUpdated) S
    IF @msg IS NOT NULL
    BEGIN
      SET @msg = concat('Optimistic concurrency conflict detected for search parameters: ', @msg) 
      ROLLBACK TRANSACTION;
      THROW 50001, @msg, 1
    END
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
        VALUES (I.Uri, I.Status, @LastUpdated, I.IsPartiallySupported);

  SET @msg = 'LastUpdated='+convert(varchar(23),@LastUpdated,126)+' Merged='+convert(varchar,@@rowcount)

  IF @InputTrancount = 0 COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Action='Merge',@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.MergeResourcesAndSearchParams 
     @SearchParams dbo.SearchParamList READONLY
    ,@InputLastUpdated datetimeoffset(7) = NULL
    ,@ReindexId bigint = NULL
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
  
  EXECUTE dbo.MergeSearchParams @SearchParams, @InputLastUpdated, @ReindexId

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
