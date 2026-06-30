ALTER PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY, @ReindexId bigint = NULL
-- @ReindexId IS NULL - not reindex. @ReindexId IS NOT NULL - reindex.
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,(SELECT count(*) FROM @SearchParams))+' R='+isnull(convert(varchar,@ReindexId),'NULL')
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
  IF @ReindexId IS NOT NULL AND @ReindexId <> 0 -- TODO: remove AND @ReindexId <> 0 after deployment
     AND NOT EXISTS (SELECT 1 FROM dbo.JobQueue WHERE PartitionId = @ReindexId % 16 AND QueueType = 6 AND JobId = @ReindexId AND Status = 1)
    RAISERROR('Reindex job is not running', 18, 127)

  IF @ReindexId IS NULL OR @ReindexId = 0 -- TODO: remove OR @ReindexId = 0 after deployment
  BEGIN
    -- Check if reindex job is running
    EXECUTE dbo.GetActiveJobs @QueueType = 6, @IsExistsCheck = 1, @GroupId = @ActiveJobId OUT
    SET @msg = 'Changes to search parameters are not allowed while a reindex job is ongoing. Wait for the reindex job with Id: '+convert(varchar,@ActiveJobId)+' to finish, or cancel it'
    IF @ActiveJobId IS NOT NULL THROW 50002, @msg, 1

    -- Check for concurrency conflicts using LastUpdated
    SET @MaxLastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)
    IF @MaxLastUpdated <> @ExpectedLastUpdated
    BEGIN
      SET @msg = 'Optimistic concurrency conflict detected : expected last updated = '+convert(varchar(23),@ExpectedLastUpdated,126)+' max last updated = '+convert(varchar(23),@MaxLastUpdated,126);
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
