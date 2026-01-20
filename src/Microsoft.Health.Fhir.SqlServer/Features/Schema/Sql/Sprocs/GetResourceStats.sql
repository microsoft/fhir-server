--DROP PROCEDURE dbo.GetResourceStats
GO
CREATE PROCEDURE dbo.GetResourceStats @StartDate datetime = NULL, @EndDate datetime = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceStats'
       ,@Mode varchar(200) = 'S='+isnull(convert(varchar(30),@StartDate,121),'NULL')+' E='+isnull(convert(varchar(30),@EndDate,121),'NULL')
       ,@st datetime = getUTCdate()

DECLARE @StartId bigint = NULL
       ,@EndId bigint = NULL

BEGIN TRY
  -- Convert dates to surrogate IDs for filtering
  IF @StartDate IS NOT NULL
    SET @StartId = convert(bigint, datediff_big(millisecond, '0001-01-01', @StartDate)) * 80000

  IF @EndDate IS NOT NULL
    SET @EndId = convert(bigint, datediff_big(millisecond, '0001-01-01', @EndDate)) * 80000 + 79999

  SELECT RT.Name AS ResourceType
        ,TotalCount = count(*)
        ,ActiveCount = sum(CASE WHEN R.IsHistory = 0 AND R.IsDeleted = 0 THEN 1 ELSE 0 END)
    FROM dbo.Resource R
         JOIN dbo.ResourceType RT ON RT.ResourceTypeId = R.ResourceTypeId
    WHERE (@StartId IS NULL OR R.ResourceSurrogateId >= @StartId)
      AND (@EndId IS NULL OR R.ResourceSurrogateId <= @EndId)
    GROUP BY
         RT.Name
    ORDER BY
         RT.Name

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
