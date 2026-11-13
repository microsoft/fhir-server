--DROP PROCEDURE dbo.GetSearchParamCacheUpdateEvents
GO
CREATE PROCEDURE dbo.GetSearchParamCacheUpdateEvents @UpdateProcess varchar(100), @UpdateEventsSince datetime, @ActiveHostsSince datetime
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'Process='+@UpdateProcess+' EventsSince='+convert(varchar(23),@UpdateEventsSince,126)+' HostsSince='+convert(varchar(23),@ActiveHostsSince,126)
       ,@st datetime = getUTCdate()

SELECT EventDate
      ,EventText = CASE WHEN Process = @UpdateProcess AND EventDate > @UpdateEventsSince THEN EventText ELSE NULL END
      ,HostName
  FROM dbo.EventLog
  WHERE EventDate > @ActiveHostsSince

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Rows=@@rowcount,@Start=@st
GO
INSERT INTO dbo.Parameters (Id, Char) SELECT 'GetSearchParamCacheUpdateEvents', 'LogEvent'
GO
