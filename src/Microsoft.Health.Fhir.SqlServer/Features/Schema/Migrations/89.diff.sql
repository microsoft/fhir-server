ALTER PROCEDURE dbo.PutJobHeartbeat @QueueType tinyint, @JobId bigint, @Version bigint, @CancelRequested bit = 0 OUTPUT
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobHeartbeat'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16

SET @Mode = 'Q='+convert(varchar,@QueueType)+' J='+convert(varchar,@JobId)+' P='+convert(varchar,@PartitionId)+' V='+convert(varchar,@Version)

BEGIN TRY
  UPDATE dbo.JobQueue
    SET @CancelRequested = CancelRequested
       ,HeartbeatDate = getUTCdate()
    WHERE QueueType = @QueueType
      AND PartitionId = @PartitionId
      AND JobId = @JobId
      AND Status = 1
      AND Version = @Version
  SET @Rows = @@rowcount
  
  IF @Rows = 0 AND NOT EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId AND Version = @Version AND Status IN (2,3,4))
  BEGIN
    IF EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId)
      THROW 50412, 'Precondition failed', 1
    ELSE
      THROW 50404, 'Job record not found', 1
  END

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

    SET @MaxJobId = isnull((SELECT TOP 1 JobId FROM dbo.JobQueue WHERE QueueType = @QueueType ORDER BY JobId DESC),0)
  
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
            ,JobId
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
