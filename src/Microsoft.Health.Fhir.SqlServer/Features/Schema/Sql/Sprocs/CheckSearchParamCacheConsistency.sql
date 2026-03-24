--DROP PROCEDURE dbo.CheckSearchParamCacheConsistency
GO
CREATE OR ALTER PROCEDURE dbo.CheckSearchParamCacheConsistency
    @TargetSearchParamLastUpdated varchar(100)
   ,@StalenessThresholdMinutes int = 10
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = '@Target='+@TargetSearchParamLastUpdated+' @Staleness='+convert(varchar,@StalenessThresholdMinutes)
       ,@st datetime = getUTCdate()
       ,@Txt nvarchar(3500)
       ,@BehindHosts nvarchar(2000)
       ,@TotalActiveHosts int
       ,@ConvergedHosts int = 0

BEGIN TRY
  DECLARE @HostStatus TABLE
  (
    HostName varchar(256) NOT NULL PRIMARY KEY,
    IsConverged bit NOT NULL
  )

  INSERT INTO @HostStatus (HostName, IsConverged)
    SELECT HostName
          ,max(CASE WHEN Process = 'SearchParameterCacheRefresh'
                         AND Status = 'End'
                         AND EventText LIKE 'SearchParamLastUpdated=%'
                         AND EventText >= 'SearchParamLastUpdated=' + @TargetSearchParamLastUpdated
                    THEN 1 ELSE 0 END) AS IsConverged
      FROM dbo.EventLog
      WHERE EventDate > dateadd(minute,-@StalenessThresholdMinutes,getUTCdate())
        AND HostName IS NOT NULL
      GROUP BY HostName

  SELECT @TotalActiveHosts = count(*)
        ,@ConvergedHosts = isnull(sum(IsConverged),0)
    FROM @HostStatus

  SELECT @BehindHosts = string_agg(convert(nvarchar(256), HostName), ',')
    FROM @HostStatus
    WHERE IsConverged = 0

  IF @BehindHosts IS NOT NULL AND len(@BehindHosts) > 1900
    SET @BehindHosts = left(@BehindHosts, 1897) + '...'

  SELECT @TotalActiveHosts AS TotalActiveHosts, @ConvergedHosts AS ConvergedHosts

  SET @Txt = N'TotalActiveHosts='+convert(varchar,@TotalActiveHosts)+N' ConvergedHosts='+convert(varchar,@ConvergedHosts)
  IF @BehindHosts IS NOT NULL AND len(@BehindHosts) > 0
    SET @Txt = @Txt + N' BehindHosts=' + @BehindHosts

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Text=@Txt
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
INSERT INTO Parameters (Id,Char) SELECT 'SearchParameterCacheRefresh','LogEvent'
GO
