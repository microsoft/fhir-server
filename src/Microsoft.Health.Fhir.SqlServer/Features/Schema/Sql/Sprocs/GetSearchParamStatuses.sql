CREATE PROCEDURE dbo.GetSearchParamStatuses @LastUpdated datetimeoffset(7) = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'GetSearchParamStatuses'
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@msg varchar(100)

BEGIN TRY
  SET @LastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)

  SET @msg = 'LastUpdated='+substring(convert(varchar,@LastUpdated),1,23)

  SELECT SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Action='Select',@Target='SearchParam',@Text=@msg
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
INSERT INTO dbo.Parameters (Id,Char) SELECT 'GetSearchParamStatuses', 'LogEvent'
GO
