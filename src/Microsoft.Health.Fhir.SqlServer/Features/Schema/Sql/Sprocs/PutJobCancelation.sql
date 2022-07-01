--DROP PROCEDURE dbo.PutJobCancelation
GO
CREATE PROCEDURE dbo.PutJobCancelation @QueueType tinyint, @GroupId bigint = NULL, @JobId bigint = NULL
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
