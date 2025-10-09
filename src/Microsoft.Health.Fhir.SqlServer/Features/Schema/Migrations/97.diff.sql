--DROP PROCEDURE dbo.GetResourceSurrogateIdRanges
GO
CREATE OR ALTER PROCEDURE dbo.GetResourceSurrogateIdRanges @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @RangeSize int, @NumberOfRanges int = 100, @Up bit = 1, @ActiveOnly bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdRanges'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' R='+isnull(convert(varchar,@RangeSize),'NULL')
                           +' UP='+isnull(convert(varchar,@Up),'NULL')
                           +' AO='+isnull(convert(varchar,@ActiveOnly),'NULL')
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
                        AND (@ActiveOnly = 0 OR (IsHistory = 0 AND IsDeleted = 0))
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
                        AND (@ActiveOnly = 0 OR (IsHistory = 0 AND IsDeleted = 0))
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