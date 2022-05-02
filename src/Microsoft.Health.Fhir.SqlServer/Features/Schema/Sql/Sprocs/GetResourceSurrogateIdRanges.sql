--DROP PROCEDURE dbo.GetResourceSurrogateIdRanges
GO
CREATE PROCEDURE dbo.GetResourceSurrogateIdRanges @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @UnitSize int
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdRanges'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' U='+isnull(convert(varchar,@UnitSize),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  SELECT UnitId
        ,min(ResourceSurrogateId)
        ,max(ResourceSurrogateId)
        ,count(*)
    FROM (SELECT UnitId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
                ,ResourceSurrogateId
            FROM dbo.Resource
            WHERE ResourceTypeId = @ResourceTypeId
              AND IsHistory = 0
              AND ResourceSurrogateId >= @StartId
              AND ResourceSurrogateId < @EndId
         ) A
    GROUP BY
         UnitId
    ORDER BY
         UnitId
    OPTION (MAXDOP 8) -- 0 7:17 -- 1 29:14 -- 8 6:43

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
