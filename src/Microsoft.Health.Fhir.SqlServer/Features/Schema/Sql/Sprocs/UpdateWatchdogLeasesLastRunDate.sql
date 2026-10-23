--DROP PROCEDURE dbo.UpdateWatchdogLeasesLastRunDate
GO
CREATE PROCEDURE dbo.UpdateWatchdogLeasesLastRunDate
    @Watchdog varchar(100)
   ,@LastRunDate datetime
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'WD='+isnull(@Watchdog,'NULL')+' LRD='+isnull(convert(varchar(23),@LastRunDate,126),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.WatchdogLeases SET LastRunDate = @LastRunDate WHERE Watchdog = @Watchdog

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
