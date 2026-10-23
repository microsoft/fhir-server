--DROP PROCEDURE dbo.GetWatchdogLeasesLastRunDate
GO
CREATE PROCEDURE dbo.GetWatchdogLeasesLastRunDate
    @Watchdog varchar(100)
   ,@LastRunDate datetime OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'W='+isnull(@Watchdog,'NULL')
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)

BEGIN TRY
  SET @LastRunDate = (SELECT LastRunDate FROM dbo.WatchdogLeases WHERE Watchdog = @Watchdog)
  SET @msg = 'LRD='+isnull(convert(varchar(23),@LastRunDate,126),'NULL')

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@msg
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
