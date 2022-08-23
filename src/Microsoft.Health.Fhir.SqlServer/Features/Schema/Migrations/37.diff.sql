--DROP PROCEDURE dbo.PutJobHeartbeat
GO
CREATE OR ALTER PROCEDURE dbo.PutJobHeartbeat @QueueType tinyint, @JobId bigint, @Version bigint, @Data bigint = NULL, @CurrentResult varchar(max) = NULL, @CancelRequested bit = 0 OUTPUT
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobHeartbeat'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16

SET @Mode = 'Q='+convert(varchar,@QueueType)+' J='+convert(varchar,@JobId)+' P='+convert(varchar,@PartitionId)+' V='+convert(varchar,@Version)+' D='+isnull(convert(varchar,@Data),'NULL')

BEGIN TRY
  IF @CurrentResult IS NULL
    UPDATE dbo.JobQueue
      SET @CancelRequested = CancelRequested
         ,HeartbeatDate = getUTCdate()
         ,Data = isnull(@Data,Data)
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 1
        AND Version = @Version
  ELSE
    UPDATE dbo.JobQueue
      SET @CancelRequested = CancelRequested
         ,HeartbeatDate = getUTCdate()
         ,Data = isnull(@Data,Data)
         ,Result = @CurrentResult
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 1
        AND Version = @Version
  
  SET @Rows = @@rowcount
  
  IF @Rows = 0
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
