--DROP PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
GO
CREATE PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourcesByTypeAndSurrogateIdRange'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  SELECT RawResource 
    FROM dbo.Resource 
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
      AND IsHistory = 0 
      AND IsDeleted = 0

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
