--DROP PROCEDURE dbo.GetResourceSurrogateIdRanges
GO
CREATE PROCEDURE dbo.GetResourceSurrogateIdRanges @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @RangeSize int, @NumberOfRanges int = 100, @Up bit = 1
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdRanges'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' R='+isnull(convert(varchar,@RangeSize),'NULL')
                           +' UP='+isnull(convert(varchar,@Up),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  IF @Up = 1
    SELECT RangeId
          ,min(ResourceSurrogateId)
          ,max(ResourceSurrogateId)
          ,count(*)
      FROM (SELECT RangeId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0)
                  ,ResourceSurrogateId
              FROM (SELECT TOP (@RangeSize * @NumberOfRanges)
                           ResourceSurrogateId
                      FROM dbo.Resource
                      WHERE ResourceTypeId = @ResourceTypeId
                        AND ResourceSurrogateId >= @StartId
                        AND ResourceSurrogateId <= @EndId
                      ORDER BY
                           ResourceSurrogateId
                   ) A
           ) A
      GROUP BY
           RangeId
      OPTION (MAXDOP 1)
  ELSE
    SELECT RangeId
          ,min(ResourceSurrogateId)
          ,max(ResourceSurrogateId)
          ,count(*)
      FROM (SELECT RangeId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @RangeSize), 0)
                  ,ResourceSurrogateId
              FROM (SELECT TOP (@RangeSize * @NumberOfRanges)
                           ResourceSurrogateId
                      FROM dbo.Resource
                      WHERE ResourceTypeId = @ResourceTypeId
                        AND ResourceSurrogateId >= @StartId
                        AND ResourceSurrogateId <= @EndId
                      ORDER BY
                           ResourceSurrogateId DESC
                   ) A
           ) A
      GROUP BY
           RangeId
      OPTION (MAXDOP 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 
THROW -- Real error is before 1750, cannot trap in SQL.
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
