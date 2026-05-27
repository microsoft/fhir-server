CREATE PROCEDURE dbo.MergeSearchParams @SearchParams dbo.SearchParamList READONLY, @ReindexId bigint = -1
-- @ReindexId = -1 old code, @ReindexId = 0 - new code but not reindex. @ReindexId > 0 - new code and reindex.
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
