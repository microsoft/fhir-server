ALTER PROCEDURE dbo.GetActiveJobs @QueueType tinyint, @GroupId bigint = NULL OUT, @ReturnParentOnly bit = 0, @IsExistsCheck bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'GetActiveJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           + ' R='+convert(varchar, @ReturnParentOnly)
                           + ' E='+convert(varchar, @IsExistsCheck)
       ,@st datetime = getUTCdate()
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@Rows int = 0

DECLARE @JobIds TABLE (Id bigint PRIMARY KEY, GroupId bigint)

BEGIN TRY
  SET @PartitionId = @MaxPartitions * rand()

  -- gfor exists check exit immediately when any row found
  WHILE @LookedAtPartitions < @MaxPartitions AND (@IsExistsCheck = 0 OR @IsExistsCheck = 1 AND @Rows = 0)
  BEGIN
    IF @GroupId IS NULL
      INSERT INTO @JobIds SELECT JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND Status IN (0,1)
    ELSE
      INSERT INTO @JobIds SELECT JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND GroupId = @GroupId AND Status IN (0,1)

    SET @Rows += @@rowcount

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions += 1 
  END

  IF @IsExistsCheck = 1
  BEGIN
    SET @GroupId = (SELECT TOP 1 GroupId FROM @JobIds ORDER BY GroupId DESC)
    RETURN
  END

  IF @Rows > 0
  BEGIN
    IF @ReturnParentOnly = 1
      DELETE FROM @JobIds WHERE Id <> (SELECT TOP 1 GroupId FROM @JobIds ORDER BY GroupId DESC)
    
    SET @GroupId = (SELECT TOP 1 GroupId FROM @JobIds ORDER BY GroupId DESC)

    EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY, @ReindexId bigint = -1
-- @ReindexId = -1 old code, @ReindexId = 0 - new codebut not reindex. @ReindexId > 0 - new code and reindex.
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,(SELECT count(*) FROM @SearchParams))+' R='+convert(varchar,@ReindexId)
       ,@st datetime = getUTCdate()
       ,@LastUpdated datetimeoffset(7) = convert(datetimeoffset(7), sysUTCdatetime())
       ,@MaxLastUpdated datetimeoffset(7)
       ,@msg varchar(4000)
       ,@Rows int
       ,@Uri varchar(4000)
       ,@Status varchar(20)
       ,@InputTrancount int = @@trancount
       ,@ActiveJobId bigint
       ,@ExpectedLastUpdated datetimeoffset(7) = (SELECT max(LastUpdated) FROM @SearchParams)

SET @Mode = @Mode +' L='+isnull(convert(varchar(23),@ExpectedLastUpdated,126),'NULL')

BEGIN TRY
  IF @ReindexId IS NULL RAISERROR('@ReindexId cannot be null', 18, 127)

  DECLARE @SearchParamsCopy dbo.SearchParamList
  INSERT INTO @SearchParamsCopy SELECT * FROM @SearchParams
  WHILE EXISTS (SELECT * FROM @SearchParamsCopy)
  BEGIN
    SELECT TOP 1 @Uri = Uri, @Status = Status FROM @SearchParamsCopy
    SET @msg = 'Status='+@Status+' Uri='+@Uri
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@msg
    DELETE FROM @SearchParamsCopy WHERE Uri = @Uri
  END

  IF @InputTrancount = 0 -- transaction can start in MergeResourcesAndSearchParams
  BEGIN 
    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    BEGIN TRANSACTION
  END

  -- check if job id is valid
  IF @ReindexId > 0
     AND NOT EXISTS (SELECT 1 FROM dbo.JobQueue WHERE PartitionId = @ReindexId % 16 AND QueueType = 6 AND JobId = @ReindexId AND Status = 1)
    RAISERROR('Reindex job is not running', 18, 127)

  IF @ReindexId < 0 -- this can be ivoked for new and old code.
  BEGIN
    EXECUTE dbo.GetActiveJobs @QueueType = 6, @IsExistsCheck = 1, @GroupId = @ActiveJobId OUT
    SET @msg = 'Reindex job is in progress. Job Id='+convert(varchar,@ActiveJobId)
    IF @ActiveJobId IS NOT NULL THROW 50002, @msg, 1
  END

  -- Check for concurrency conflicts using LastUpdated
  -- Ignore any checks for reindex, as it owns statuses when it runs.
  IF @ReindexId = 0 -- max(LastUpdated) logic
  BEGIN
    SET @MaxLastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)
    IF @MaxLastUpdated <> @ExpectedLastUpdated
    BEGIN
      SET @msg = 'Optimistic concurrency conflict detected : expected last updated = '+convert(varchar(23),@ExpectedLastUpdated,126)+' max last updated = '+convert(varchar(23),@MaxLastUpdated,126);
      THROW 50001, @msg, 1
    END
  END

  -- Remove this old logic when code starts using max last updated
  IF @ReindexId = -1
  BEGIN
    -- Only the top 60 are included in the message to avoid hitting the 8000 character limit, but all conflicts will cause the transaction to roll back
    SELECT @msg = string_agg(S.Uri, ', ') 
      FROM (
        SELECT TOP 60 S.Uri
          FROM @SearchParams I JOIN dbo.SearchParam S ON S.Uri = I.Uri
          WHERE I.LastUpdated != S.LastUpdated) S
    IF @msg IS NOT NULL
    BEGIN
      SET @msg = concat('Optimistic concurrency conflict detected for search parameters: ', @msg);
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
  SET @Rows = @@rowcount
  
  SET @msg = 'LastUpdated='+convert(varchar(23),@LastUpdated,126)

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
