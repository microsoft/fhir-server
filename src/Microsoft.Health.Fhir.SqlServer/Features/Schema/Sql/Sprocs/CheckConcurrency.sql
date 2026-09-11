--DROP PROCEDURE dbo.CheckConcurrency
GO
CREATE PROCEDURE dbo.CheckConcurrency 
AS
set nocount on
DECLARE @SP varchar(100) = 'CheckConcurrency'
       ,@Mode varchar(200) = ''
       ,@st datetime = getUTCdate()
       ,@OptimalConcurrency int = isnull((SELECT Number FROM Parameters WHERE Id = 'Global.OptimalConcurrentCalls'), 256)
       ,@CurrentConcurrency int
       ,@msg varchar(1000)

BEGIN TRY
  SET @CurrentConcurrency = (SELECT count(*) FROM sys.dm_exec_sessions WHERE status <> 'sleeping')
  IF @CurrentConcurrency > @OptimalConcurrency
  BEGIN
    SET @msg = 'Number of concurrent calls = '+convert(varchar,@CurrentConcurrency)+' is above optimal = '+convert(varchar,@OptimalConcurrency)+'.';
    THROW 50410, @msg, 1 
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@StartTime=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
