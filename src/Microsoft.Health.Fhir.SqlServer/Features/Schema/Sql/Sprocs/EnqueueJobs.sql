--DROP PROCEDURE dbo.EnqueueJobs
GO
CREATE PROCEDURE dbo.EnqueueJobs @QueueType tinyint, @Definitions StringList READONLY, @GroupId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'EnqueueJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' D='+convert(varchar,(SELECT count(*) FROM @Definitions))
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
       ,@st datetime = getUTCdate()
       ,@Lock varchar(100) = 'EnqueueJobs_'+convert(varchar,@QueueType)
       ,@MaxJobId bigint
       ,@Rows int

BEGIN TRY
  BEGIN TRANSACTION  

  EXECUTE sp_getapplock @Lock, 'Exclusive'

  SET @MaxJobId = isnull((SELECT TOP 1 JobId FROM dbo.JobQueue WHERE QueueType = @QueueType ORDER BY JobId DESC),0)
    
  INSERT INTO dbo.JobQueue
      (
           QueueType
          ,GroupId
          ,JobId
          ,Definition
          ,DefinitionHash
      )
    SELECT @QueueType
          ,GroupId = isnull(@GroupId,@MaxJobId+1)
          ,JobId
          ,Definition
          ,DefinitionHash
      FROM (SELECT JobId = @MaxJobId + row_number() OVER (ORDER BY substring(String,1,1)), DefinitionHash = hashbytes('SHA1',String), Definition = String FROM @Definitions) A
      WHERE NOT EXISTS (SELECT * FROM dbo.JobQueue B WHERE B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5)
  SET @Rows = @@rowcount

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
