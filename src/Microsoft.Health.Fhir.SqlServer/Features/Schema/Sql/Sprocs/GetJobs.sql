--DROP PROCEDURE dbo.GetJobs
GO
CREATE PROCEDURE dbo.GetJobs
   @QueueType        tinyint
  ,@JobId            bigint  = NULL
  ,@JobIds           BigintList READONLY
  ,@GroupId          bigint  = NULL
  ,@ReturnDefinition bit     = 1
AS
set nocount on
DECLARE @SP varchar(100) = 'GetJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' J='+isnull(convert(varchar,@JobId),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
       ,@st datetime = getUTCdate()
       ,@PartitionId tinyint = @JobId % 16

BEGIN TRY
  IF @JobId IS NULL AND @GroupId IS NULL AND NOT EXISTS (SELECT * FROM @JobIds)
    RAISERROR('@JobId = NULL and @GroupId = NULL and @JobIds is empty',18,127)

  IF @JobId IS NOT NULL
    SELECT GroupId
          ,JobId
          ,Definition = CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END
          ,Version
          ,Status
          ,Priority
          ,Data
          ,Result
          ,CreateDate
          ,StartDate
          ,EndDate
          ,HeartbeatDate
          ,CancelRequested
      FROM dbo.JobQueue -- This can return only one item
      WHERE QueueType = @QueueType
        AND PartitionId = @PartitionId
        AND JobId = isnull(@JobId,-1)    
        AND Status <> 5 -- not archived
  ELSE 
    IF @GroupId IS NOT NULL 
      SELECT GroupId
            ,JobId
            ,Definition = CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END
            ,Version
            ,Status
            ,Priority
            ,Data
            ,Result
            ,CreateDate
            ,StartDate
            ,EndDate
            ,HeartbeatDate
            ,CancelRequested
        FROM dbo.JobQueue WITH (INDEX = IX_QueueType_GroupId) -- Force access by group id -- This can return more than one item
        WHERE QueueType = @QueueType
          AND GroupId = isnull(@GroupId,-1) 
          AND Status <> 5 -- not archived
    ELSE
      SELECT GroupId
            ,JobId
            ,Definition = CASE WHEN @ReturnDefinition = 1 THEN Definition ELSE NULL END
            ,Version
            ,Status
            ,Priority
            ,Data
            ,Result
            ,CreateDate
            ,StartDate
            ,EndDate
            ,HeartbeatDate
            ,CancelRequested
      FROM dbo.JobQueue -- This can return only one item
        WHERE QueueType = @QueueType
          AND JobId IN (SELECT Id FROM @JobIds)
          AND PartitionId = JobId % 16
          AND Status <> 5 -- not archived

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
