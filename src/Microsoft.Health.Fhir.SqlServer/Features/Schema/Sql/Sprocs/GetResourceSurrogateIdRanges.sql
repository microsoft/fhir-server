--DROP PROCEDURE dbo.GetResourceSurrogateIdRanges
GO
CREATE PROCEDURE dbo.GetResourceSurrogateIdRanges @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @UnitSize int, @NumberOfRanges int = 100
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdRanges'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' U='+isnull(convert(varchar,@UnitSize),'NULL')
       ,@st datetime = getUTCdate()
       ,@IntStartId bigint
       ,@IntEndId bigint

BEGIN TRY
  SELECT UnitId
        ,min(ResourceSurrogateId)
        ,max(ResourceSurrogateId)
        ,count(*)
    FROM (SELECT UnitId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
                ,ResourceSurrogateId
            FROM (SELECT TOP (@UnitSize * @NumberOfRanges)
                         ResourceSurrogateId
                    FROM dbo.Resource
                    WHERE ResourceTypeId = @ResourceTypeId
                      AND ResourceSurrogateId >= @StartId
                      AND ResourceSurrogateId < @EndId
                    ORDER BY
                         ResourceSurrogateId
                 ) A
         ) A
    GROUP BY
         UnitId
    ORDER BY
         UnitId
    OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--set nocount on
--DECLARE @Ranges TABLE (UnitId int, MinId bigint, MaxId bigint, Cnt int)
--DECLARE @MaxId bigint = 0
--       ,@msg varchar(1000)
--       ,@Loop int = 0

--WHILE @MaxId IS NOT NULL AND @Loop < 1000
--BEGIN
--  DELETE FROM @Ranges
--  INSERT INTO @Ranges
--    EXECUTE GetResourceSurrogateIdRanges 96, @MaxId, 9e18, 100000
--  SET @MaxId = (SELECT TOP 1 MaxId FROM @Ranges ORDER BY MaxId DESC) + 1
--  SET @msg = 'Loop='+convert(varchar,@Loop)+' MaxId='+convert(varchar,@MaxId)
--  RAISERROR(@msg,0,1) WITH NOWAIT
--  SET @Loop += 1
--END
