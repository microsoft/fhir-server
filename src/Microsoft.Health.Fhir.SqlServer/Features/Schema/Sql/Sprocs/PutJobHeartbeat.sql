CREATE PROCEDURE dbo.PutJobHeartbeat @QueueType tinyint, @JobId bigint, @Version bigint, @Data bigint = NULL, @CurrentResult varchar(max) = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'PutJobHeartbeat'
       ,@Mode varchar(100)
       ,@st datetime2 = SYSUTCDATETIME()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16

SET @Mode = 'Q='+convert(varchar,@QueueType)+' J='+convert(varchar,@JobId)+' P='+convert(varchar,@PartitionId)+' V='+convert(varchar,@Version)+' D='+isnull(convert(varchar,@Data),'NULL')

BEGIN TRY
  IF @CurrentResult IS NULL
    UPDATE dbo.JobQueue
      SET HeartbeatDate = SYSUTCDATETIME()
         ,Data = isnull(@Data,Data)
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 1
        AND Version = @Version
  ELSE
    UPDATE dbo.JobQueue
      SET HeartbeatDate = SYSUTCDATETIME()
         ,Data = isnull(@Data,Data)
         ,Result = @CurrentResult
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = @JobId
        AND Status = 1
        AND Version = @Version

  SET @Rows = @@rowcount

  IF @Rows = 0
    THROW 50412, 'Precondition failed', 1

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
