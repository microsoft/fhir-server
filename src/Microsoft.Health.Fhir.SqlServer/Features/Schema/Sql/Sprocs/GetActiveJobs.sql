--DROP PROCEDURE dbo.GetActiveJobs
GO
CREATE PROCEDURE dbo.GetActiveJobs @QueueType tinyint, @GroupId bigint = NULL OUT, @ReturnParentOnly bit = 0, @IsExistsCheck bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'GetActiveJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           + ' R='+convert(varchar, @ReturnParentOnly)
                           + ' E='+convert(varchar, @IsExistsCheck)
       ,@st datetime = getUTCdate()
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@Rows int = 0

DECLARE @JobIds TABLE (Id bigint PRIMARY KEY, GroupId bigint)

BEGIN TRY
  SET @PartitionId = @MaxPartitions * rand()

  -- gfor exists check exit immediately when any row found
  WHILE @LookedAtPartitions < @MaxPartitions AND (@IsExistsCheck = 0 OR @IsExistsCheck = 1 AND @Rows = 0)
  BEGIN
    IF @GroupId IS NULL
      INSERT INTO @JobIds SELECT JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND Status IN (0,1)
    ELSE
      INSERT INTO @JobIds SELECT JobId, GroupId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND GroupId = @GroupId AND Status IN (0,1)

    SET @Rows += @@rowcount

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions += 1 
  END

  IF @IsExistsCheck = 1
  BEGIN
    SET @GroupId = (SELECT TOP 1 GroupId FROM @JobIds ORDER BY GroupId DESC)
    RETURN
  END

  IF @Rows > 0
  BEGIN
    IF @ReturnParentOnly = 1
      DELETE FROM @JobIds WHERE Id <> (SELECT TOP 1 GroupId FROM @JobIds ORDER BY GroupId DESC)

    SET @GroupId = (SELECT TOP 1 GroupId FROM @JobIds ORDER BY GroupId DESC)

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
