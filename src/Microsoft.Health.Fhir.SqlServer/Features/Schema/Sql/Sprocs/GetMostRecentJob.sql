--DROP PROCEDURE dbo.GetMostRecentJob
GO
CREATE OR ALTER PROCEDURE dbo.GetMostRecentJob @QueueType tinyint
AS
set nocount on
DECLARE @SP varchar(100) = 'GetMostRecentJob'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
       ,@st datetime = getUTCdate()
       ,@JobIds BigintList
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@Rows int = 0

DECLARE @JobRecords TABLE (Id bigint PRIMARY KEY, GroupId bigint)

BEGIN TRY
  SET @PartitionId = @MaxPartitions * rand()

  -- for exists check exit immediately when any row found
  WHILE @LookedAtPartitions < @MaxPartitions
  BEGIN
    INSERT INTO @JobRecords SELECT TOP 1 JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType ORDER BY GroupId DESC

    SET @Rows += @@rowcount

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions += 1 
  END

  IF @Rows > 0
  BEGIN
    INSERT INTO @JobIds SELECT TOP 1 GroupId FROM @JobRecords ORDER BY GroupId DESC

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
