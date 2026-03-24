--DROP PROCEDURE dbo.CheckSearchParamCacheConsistency
GO
CREATE OR ALTER PROCEDURE dbo.CheckSearchParamCacheConsistency
    @TargetSearchParamLastUpdated varchar(100)
   ,@SyncStartDate datetime2(7)
   ,@ActiveHostsSince datetime2(7)
   ,@StalenessThresholdMinutes int = 10
AS
set nocount on
SELECT HostName
      ,CAST(NULL AS datetime2(7)) AS SyncEventDate
      ,CAST(NULL AS nvarchar(3500)) AS EventText
  FROM dbo.EventLog
  WHERE EventDate >= @ActiveHostsSince
    AND HostName IS NOT NULL
    AND Process = 'DequeueJob'

UNION ALL

SELECT HostName
      ,EventDate
      ,EventText
  FROM dbo.EventLog
  WHERE EventDate >= @SyncStartDate
    AND HostName IS NOT NULL
    AND Process = 'SearchParameterCacheRefresh'
    AND Status = 'End'
GO
INSERT INTO dbo.Parameters (Id, Char) SELECT 'DequeueJob', 'LogEvent' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'DequeueJob')
GO
INSERT INTO Parameters (Id,Char) SELECT 'SearchParameterCacheRefresh','LogEvent'
GO
