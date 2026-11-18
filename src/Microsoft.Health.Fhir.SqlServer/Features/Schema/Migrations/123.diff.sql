CREATE PROCEDURE dbo.GetJobStatus
   @QueueType tinyint = NULL
  ,@PageSize int = 10
  ,@AfterGroupId bigint = NULL
  ,@AfterQueueType tinyint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'GetJobStatus'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  IF @PageSize IS NULL OR @PageSize < 1
    THROW 50400, '@PageSize must be positive.', 1;

  IF @QueueType IS NOT NULL AND @QueueType NOT IN (1,2,4,5,6)
    THROW 50400, 'Unsupported @QueueType.', 1;

  IF (@AfterGroupId IS NULL AND @AfterQueueType IS NOT NULL)
     OR (@AfterGroupId IS NOT NULL AND @AfterQueueType IS NULL)
     OR @AfterQueueType NOT IN (1,2,4,5,6)
     OR (@QueueType IS NOT NULL AND @AfterQueueType <> @QueueType)
    THROW 50400, 'Invalid job status cursor.', 1;

  ;WITH JobGroups AS
  (
    SELECT TOP (@PageSize) Q.QueueType, G.GroupId
    FROM (VALUES (CONVERT(tinyint,1)),(CONVERT(tinyint,2)),(CONVERT(tinyint,4)),(CONVERT(tinyint,5)),(CONVERT(tinyint,6))) Q(QueueType)
    CROSS APPLY
    (
      SELECT TOP (@PageSize) J.GroupId
      FROM dbo.JobQueue J
      WHERE J.QueueType = Q.QueueType
        AND J.Status <> 5
        AND (@AfterGroupId IS NULL OR J.GroupId <= @AfterGroupId)
        AND (@AfterGroupId IS NULL OR J.GroupId < @AfterGroupId OR Q.QueueType < @AfterQueueType)
      GROUP BY J.GroupId
      ORDER BY J.GroupId DESC
    ) G
    WHERE @QueueType IS NULL OR Q.QueueType = @QueueType
    ORDER BY G.GroupId DESC, Q.QueueType DESC
  )
  SELECT G.GroupId
        ,G.QueueType
        ,Status = min(J.Status)
        ,CreateDate = min(J.CreateDate)
        ,EndDate = max(J.EndDate)
  FROM JobGroups G
    INNER JOIN dbo.JobQueue J ON J.QueueType = G.QueueType AND J.GroupId = G.GroupId
  WHERE J.Status <> 5
  GROUP BY G.GroupId, G.QueueType
  ORDER BY G.GroupId DESC, G.QueueType DESC
  OPTION (RECOMPILE)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
