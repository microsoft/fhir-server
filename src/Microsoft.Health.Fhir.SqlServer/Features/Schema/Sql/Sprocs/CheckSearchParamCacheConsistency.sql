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
       ,@Txt varchar(1000)
       ,@TotalActiveHosts int
       ,@ConvergedHosts int = 0

BEGIN TRY
  DECLARE @ActiveHosts TABLE (HostName varchar(256) NOT NULL PRIMARY KEY)
  INSERT INTO @ActiveHosts
    SELECT DISTINCT HostName
      FROM dbo.EventLog
      WHERE EventDate > dateadd(minute,-@StalenessThresholdMinutes,getUTCdate())
        AND HostName IS NOT NULL

  SET @TotalActiveHosts = (SELECT count(*) FROM @ActiveHosts)

  SELECT @ConvergedHosts = count(DISTINCT E.HostName)
    FROM dbo.EventLog E
      JOIN @ActiveHosts A ON A.HostName = E.HostName
    WHERE E.Process = 'SearchParameterCacheRefresh'
      AND E.Status = 'End'
      AND E.EventDate > dateadd(minute,-@StalenessThresholdMinutes,getUTCdate())
      AND E.EventText LIKE 'SearchParamLastUpdated=%'
      AND substring(E.EventText,26,100) >= @TargetSearchParamLastUpdated

  SELECT @TotalActiveHosts AS TotalActiveHosts, @ConvergedHosts AS ConvergedHosts

  SET @Txt = 'TotalActiveHosts='+convert(varchar,@TotalActiveHosts)+' ConvergedHosts='+convert(varchar,@ConvergedHosts)
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
