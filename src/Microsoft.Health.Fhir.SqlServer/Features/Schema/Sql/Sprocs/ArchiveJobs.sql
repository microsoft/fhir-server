--DROP PROCEDURE dbo.ArchiveJobs
GO
CREATE PROCEDURE dbo.ArchiveJobs @QueueType tinyint
AS
set nocount on
DECLARE @SP varchar(100) = 'ArchiveJobs'
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@InflightRows int = 0
       ,@Lock varchar(100) = 'DequeueJob_'+convert(varchar,@QueueType)

BEGIN TRY
  SET @PartitionId = @MaxPartitions * rand()

  BEGIN TRANSACTION
  
  EXECUTE sp_getapplock @Lock, 'Exclusive'

  WHILE @LookedAtPartitions <= @MaxPartitions
  BEGIN
    SET @InflightRows += (SELECT count(*) FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND Status IN (0,1))

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions = @LookedAtPartitions + 1 
  END

  IF @InflightRows = 0
  BEGIN
    SET @LookedAtPartitions = 0
    WHILE @LookedAtPartitions <= @MaxPartitions
    BEGIN
      UPDATE dbo.JobQueue
        SET Status = 5
        WHERE PartitionId = @PartitionId
          AND QueueType = @QueueType
          AND Status IN (2,3,4)
      SET @Rows += @@rowcount

      SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
      SET @LookedAtPartitions = @LookedAtPartitions + 1 
    END
  END

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
