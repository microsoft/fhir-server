--DROP PROCEDURE dbo.GetAllJobs
GO
CREATE PROCEDURE dbo.GetAllJobs @QueueType tinyint, @ReturnParentOnly bit = 0, @Since datetime = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'GetAllJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           + ' R='+convert(varchar, @ReturnParentOnly)
                           + ' S='+isnull(convert(varchar,@Since),'NULL')
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
    
    IF @Since IS NULL
        INSERT INTO @JobRecords SELECT JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType
    ELSE
        INSERT INTO @JobRecords SELECT JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND CreatedDate >= @Since

    SET @Rows += @@rowcount

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions += 1 
  END

  IF @Rows > 0
  BEGIN
    IF @ReturnParentOnly = 1
      DELETE FROM @JobRecords WHERE Id <> @GroupId

    INSERT INTO @JobIds SELECT Id FROM @JobRecords

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
