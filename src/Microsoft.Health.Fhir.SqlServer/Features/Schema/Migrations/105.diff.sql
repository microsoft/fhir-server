ALTER PROCEDURE dbo.PutJobCancelation @QueueType tinyint, @GroupId bigint = NULL, @JobId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobCancelation'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           +' J='+isnull(convert(varchar,@JobId),'NULL')
       ,@st datetime = getUTCdate()
       ,@Rows int
       ,@PartitionId tinyint = @JobId % 16

BEGIN TRY
  IF @JobId IS NULL AND @GroupId IS NULL
    RAISERROR('@JobId = NULL and @GroupId = NULL',18,127)

  IF @JobId IS NOT NULL
  BEGIN
    UPDATE dbo.JobQueue
      SET Status = 4 -- cancelled
         ,EndDate = getUTCdate()
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 0
    SET @Rows = @@rowcount

    IF @Rows = 0
    BEGIN
      UPDATE dbo.JobQueue
        SET CancelRequested = 1 -- It is upto job logic to determine what to do 
        WHERE QueueType = @QueueType
          AND PartitionId = @PartitionId
          AND JobId = @JobId
          AND Status = 1
      SET @Rows = @@rowcount
    END
  END
  ELSE 
  BEGIN
    UPDATE dbo.JobQueue
      SET Status = 4 -- cancelled 
         ,EndDate = getUTCdate()
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
      WHERE QueueType = @QueueType
        AND GroupId = @GroupId
        AND Status = 0
    SET @Rows = @@rowcount

    UPDATE dbo.JobQueue
      SET CancelRequested = 1 -- It is upto job logic to determine what to do
      WHERE QueueType = @QueueType
        AND GroupId = @GroupId
        AND Status = 1
    SET @Rows += @@rowcount
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.PutJobStatus @QueueType tinyint, @JobId bigint, @Version bigint, @Failed bit, @Data bigint, @FinalResult varchar(max), @RequestCancellationOnFailure bit
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobStatus'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16
       ,@GroupId bigint

SET @Mode = 'Q='+convert(varchar,@QueueType)+' J='+convert(varchar,@JobId)+' P='+convert(varchar,@PartitionId)+' V='+convert(varchar,@Version)+' F='+convert(varchar,@Failed)+' R='+isnull(@FinalResult,'NULL')

BEGIN TRY
  UPDATE dbo.JobQueue
    SET EndDate = getUTCdate()
       ,Status = CASE WHEN @Failed = 1 THEN 3 WHEN CancelRequested = 1 THEN 4 ELSE 2 END -- 2=completed 3=failed 4=cancelled
       ,Data = @Data
       ,Result = @FinalResult
       -- This call must be idempotent, so version cannot be changed.
       ,@GroupId = GroupId
    WHERE QueueType = @QueueType
      AND PartitionId = @PartitionId
      AND JobId = @JobId
      AND Status = 1
      AND Version = @Version
  SET @Rows = @@rowcount
  
  IF @Rows = 0
  BEGIN
    SET @GroupId = (SELECT GroupId FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId AND Version = @Version AND Status IN (2,3,4))
    IF @GroupId IS NULL
      IF EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId)
        THROW 50412, 'Precondition failed', 1
      ELSE
        THROW 50404, 'Job record not found', 1
  END

  IF @Failed = 1 AND @RequestCancellationOnFailure = 1
    EXECUTE dbo.PutJobCancelation @QueueType = @QueueType, @GroupId = @GroupId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.EnqueueJobs @QueueType tinyint, @Definitions StringList READONLY, @GroupId bigint = NULL, @ForceOneActiveJobGroup bit = 1, @Status tinyint = NULL, @Result varchar(max) = NULL, @StartDate datetime = NULL, @ReturnJobs bit = 1
AS
set nocount on
DECLARE @SP varchar(100) = 'EnqueueJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' D='+convert(varchar,(SELECT count(*) FROM @Definitions))
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           +' F='+isnull(convert(varchar,@ForceOneActiveJobGroup),'NULL')
                           +' S='+isnull(convert(varchar,@Status),'NULL')
       ,@st datetime = getUTCdate()
       ,@Lock varchar(100) = 'EnqueueJobs_'+convert(varchar,@QueueType)
       ,@MaxJobId bigint
       ,@MaxProcessingJobIdWithinAGroup bigint
       ,@Rows int
       ,@msg varchar(1000)
       ,@JobIds BigintList
       ,@InputRows int

BEGIN TRY
  DECLARE @Input TABLE (DefinitionHash varbinary(20) PRIMARY KEY, Definition varchar(max))
  INSERT INTO @Input SELECT DefinitionHash = hashbytes('SHA1',String), Definition = String FROM @Definitions
  SET @InputRows = @@rowcount

  INSERT INTO @JobIds
    SELECT JobId
      FROM @Input A
           JOIN dbo.JobQueue B ON B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5
  
  IF @@rowcount < @InputRows
  BEGIN
    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    IF @ForceOneActiveJobGroup = 1 AND EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND Status IN (0,1) AND (@GroupId IS NULL OR GroupId <> @GroupId))
      RAISERROR('There are other active job groups',18,127)

    IF @GroupId IS NOT NULL AND @Status <> 6 AND EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND JobId = @GroupId AND CancelRequested = 1)
      RAISERROR('The specified job group is cancelled',18,127)

    SET @MaxJobId = isnull((SELECT TOP 1 JobId FROM dbo.JobQueue WHERE QueueType = @QueueType ORDER BY JobId DESC),0)

    IF @Status = 6
    BEGIN
        SET @MaxProcessingJobIdWithinAGroup = isnull((SELECT TOP 1 JobId + 1 FROM dbo.JobQueue WHERE QueueType = @QueueType AND GroupId = @GroupId ORDER BY JobId DESC),0)
    END
  
    INSERT INTO dbo.JobQueue
        (
             QueueType
            ,GroupId
            ,JobId
            ,Definition
            ,DefinitionHash
            ,Status
            ,Result
            ,StartDate
            ,EndDate
        )
      OUTPUT inserted.JobId INTO @JobIds
      SELECT @QueueType
            ,GroupId = isnull(@GroupId,@MaxJobId+1)
            ,JobId = CASE WHEN @Status = 6 THEN @MaxProcessingJobIdWithinAGroup ELSE JobId END
            ,Definition
            ,DefinitionHash
            ,Status = isnull(@Status,0)
            ,Result = CASE WHEN @Status = 2 THEN @Result ELSE NULL END
            ,StartDate = CASE WHEN @Status = 1 THEN getUTCdate() ELSE @StartDate END
            ,EndDate = CASE WHEN @Status = 2 THEN getUTCdate() ELSE NULL END
        FROM (SELECT JobId = @MaxJobId + row_number() OVER (ORDER BY Dummy), * FROM (SELECT *, Dummy = 0 FROM @Input) A) A -- preserve input order
        WHERE NOT EXISTS (SELECT * FROM dbo.JobQueue B WITH (INDEX = IX_QueueType_DefinitionHash) WHERE B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5)
    SET @Rows = @@rowcount

    COMMIT TRANSACTION
  END

  IF @ReturnJobs = 1
    EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
INSERT INTO Parameters (Id,Char) SELECT 'EnqueueJobs','LogEvent'
GO

