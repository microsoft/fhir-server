IF col_length('dbo.WatchdogLeases', 'LastRunTime') IS NULL
  ALTER TABLE dbo.WatchdogLeases ADD LastRunTime datetime NOT NULL CONSTRAINT DF_WatchdogLeases_LastRunTime DEFAULT 0
GO
CREATE OR ALTER PROCEDURE dbo.UpdateWatchdogLeasesLastRunTime
    @Watchdog varchar(100)
   ,@LastRunTime datetime OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'WD='+isnull(@Watchdog,'NULL')
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)

BEGIN TRY
  SET @LastRunTime = (SELECT LastRunTime FROM dbo.WatchdogLeases WHERE Watchdog = @Watchdog)
  SET @msg = 'LRT='+isnull(convert(varchar(23),@LastRunTime,126),'NULL')

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@msg
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
