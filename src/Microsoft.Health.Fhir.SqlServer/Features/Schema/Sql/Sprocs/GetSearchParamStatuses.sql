CREATE PROCEDURE dbo.GetSearchParamStatuses @StartLastUpdated datetimeoffset(7) = NULL, @LastUpdated datetimeoffset(7) = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'GetSearchParamStatuses'
       ,@Mode varchar(100) = 'S='+isnull(substring(convert(varchar,@StartLastUpdated),1,23),'NULL')
       ,@st datetime = getUTCdate()
       ,@msg varchar(100)
       ,@Rows int

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL REPEATABLE READ
  
  BEGIN TRANSACTION

  SET @LastUpdated = (SELECT max(LastUpdated) FROM dbo.SearchParam)
  SET @msg = 'LastUpdated='+substring(convert(varchar,@LastUpdated),1,23)

  IF @StartLastUpdated IS NULL
    SELECT SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam
  ELSE
    SELECT SearchParamId, Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam WHERE LastUpdated > @StartLastUpdated
  
  SET @Rows = @@rowcount

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Action='Select',@Target='SearchParam',@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
INSERT INTO dbo.Parameters (Id,Char) SELECT 'GetSearchParamStatuses', 'LogEvent'
GO
