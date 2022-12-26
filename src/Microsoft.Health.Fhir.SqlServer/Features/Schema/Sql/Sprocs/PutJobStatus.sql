--DROP PROCEDURE dbo.PutJobStatus
GO
CREATE PROCEDURE dbo.PutJobStatus @QueueType tinyint, @JobId bigint, @Version bigint, @Failed bit, @Data bigint, @FinalResult varchar(max), @RequestCancellationOnFailure bit
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
  
  IF @Rows = 0 AND NOT EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND JobId = @JobId AND Version = @Version AND Status IN (2,3,4))
  BEGIN
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
