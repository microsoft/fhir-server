--DROP PROCEDURE dbo.CleanupEventLog
GO
CREATE PROCEDURE dbo.CleanupEventLog -- This sp keeps EventLog table small
WITH EXECUTE AS 'dbo' -- this is required for sys.dm_db_partition_stats access
AS
set nocount on
DECLARE @SP                    varchar(100) = 'CleanupEventLog'
       ,@Mode                  varchar(100) = ''
       ,@MaxDeleteRows         int
       ,@MaxAllowedRows        bigint
       ,@RetentionPeriodSecond int
       ,@DeletedRows           int
       ,@TotalDeletedRows      int = 0
       ,@TotalRows             int
       ,@Now                   datetime = getUTCdate()

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

BEGIN TRY
  SET @MaxDeleteRows = (SELECT Number FROM dbo.Parameters WHERE Id = 'CleanupEventLog.DeleteBatchSize')
  IF @MaxDeleteRows IS NULL
    RAISERROR('Cannot get Parameter.CleanupEventLog.DeleteBatchSize',18,127)
    
  SET @MaxAllowedRows = (SELECT Number FROM dbo.Parameters WHERE Id = 'CleanupEventLog.AllowedRows')
  IF @MaxAllowedRows IS NULL
    RAISERROR('Cannot get Parameter.CleanupEventLog.AllowedRows',18,127)
    
  SET @RetentionPeriodSecond = (SELECT Number*24*60*60 FROM dbo.Parameters WHERE Id = 'CleanupEventLog.RetentionPeriodDay')
  IF @RetentionPeriodSecond IS NULL
    RAISERROR('Cannot get Parameter.CleanupEventLog.RetentionPeriodDay',18,127)
    
  SET @TotalRows = (SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id('EventLog') AND index_id IN (0,1))
  
  SET @DeletedRows = 1
  
  WHILE @DeletedRows > 0 AND EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'CleanupEventLog.IsEnabled' AND Number = 1)
  BEGIN
    SET @DeletedRows = 0
    
    -- Do anything only if...
    IF @TotalRows - @TotalDeletedRows > @MaxAllowedRows -- row check
    BEGIN
      DELETE TOP (@MaxDeleteRows) 
        FROM dbo.EventLog WITH (PAGLOCK)
        WHERE EventDate <= dateadd(second, -@RetentionPeriodSecond, @Now) -- cannot use getdate because it is a moving target
      SET @DeletedRows = @@rowcount
      SET @TotalDeletedRows += @DeletedRows
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='EventLog',@Action='Delete',@Rows=@DeletedRows,@Text=@TotalDeletedRows
    END -- row check
  END -- While
  
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@Now
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DROP PROCEDURE CleanupEventLog
--SELECT TOP 1000 * FROM EventLog WHERE Process = 'CleanupEventLog' AND EventDate >= '2012-04-25 20:38:13.090' ORDER BY EventID DESC
--CleanupEventLog
--SELECT count(*) FROM EventLog
--UPDATE Parameters SET Number = 100 WHERE Id = 'CleanupEventLog.DeleteBatchSize'
--UPDATE Parameters SET Number = 10 WHERE Id = 'CleanupEventLog.AllowedRows'
--UPDATE Parameters SET Number = 1.0/24/60/10 WHERE Id = 'CleanupEventLog.RetentionDay'
--UPDATE Parameters SET Number = 60 WHERE Id = 'CleanupEventLog.RetentionDay'
--SELECT * FROM Parameters
