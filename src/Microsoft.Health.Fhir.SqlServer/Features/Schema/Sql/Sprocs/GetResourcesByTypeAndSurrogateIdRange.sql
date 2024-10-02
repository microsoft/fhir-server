--DROP PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
GO
CREATE PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @GlobalEndId bigint = NULL, @IncludeHistory bit = 0, @IncludeDeleted bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourcesByTypeAndSurrogateIdRange'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' GE='+isnull(convert(varchar,@GlobalEndId),'NULL')
                           +' HI='+isnull(convert(varchar,@IncludeHistory),'NULL')
                           +' DE'+isnull(convert(varchar,@IncludeDeleted),'NULL')
       ,@st datetime = getUTCdate()
       ,@DummyTop bigint = 9223372036854775807

BEGIN TRY
  DECLARE @ResourceIds TABLE (ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS PRIMARY KEY)
  DECLARE @SurrogateIds TABLE (MaxSurrogateId bigint PRIMARY KEY)

  IF @GlobalEndId IS NOT NULL AND @IncludeHistory = 0 -- snapshot view
  BEGIN
    INSERT INTO @ResourceIds
      SELECT DISTINCT ResourceId
        FROM dbo.Resource 
        WHERE ResourceTypeId = @ResourceTypeId 
          AND ResourceSurrogateId BETWEEN @StartId AND @EndId
          AND IsHistory = 1
          AND (IsDeleted = 0 OR @IncludeDeleted = 1)
        OPTION (MAXDOP 1)

    IF @@rowcount > 0
      INSERT INTO @SurrogateIds
        SELECT ResourceSurrogateId
          FROM (SELECT ResourceId, ResourceSurrogateId, RowId = row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId DESC)
                  FROM dbo.Resource WITH (INDEX = IX_Resource_ResourceTypeId_ResourceId_Version) -- w/o hint access to Resource table is inefficient when many versions are present. Hint is ignored if Resource is a view.
                  WHERE ResourceTypeId = @ResourceTypeId
                    AND ResourceId IN (SELECT TOP (@DummyTop) ResourceId FROM @ResourceIds)
                    AND ResourceSurrogateId BETWEEN @StartId AND @GlobalEndId
               ) A
          WHERE RowId = 1
            AND ResourceSurrogateId BETWEEN @StartId AND @EndId
          OPTION (MAXDOP 1, OPTIMIZE FOR (@DummyTop = 1))
  END

  SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource 
    FROM dbo.Resource
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
      AND (IsHistory = 0 OR @IncludeHistory = 1)
      AND (IsDeleted = 0 OR @IncludeDeleted = 1)
  UNION ALL
  SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource 
    FROM @SurrogateIds
         JOIN dbo.Resource ON ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId = MaxSurrogateId
    WHERE IsHistory = 1
      AND (IsDeleted = 0 OR @IncludeDeleted = 1)
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
--DECLARE @Ranges TABLE (UnitId int PRIMARY KEY, MinId bigint, MaxId bigint, Cnt int)
--INSERT INTO @Ranges
--  EXECUTE dbo.GetResourceSurrogateIdRanges 96, 0, 9e18, 90000, 10
--SELECT count(*) FROM @Ranges
--DECLARE @UnitId int
--       ,@MinId bigint
--       ,@MaxId bigint
--DECLARE @Resources TABLE (RawResource varbinary(max))
--WHILE EXISTS (SELECT * FROM @Ranges)
--BEGIN
--  SELECT TOP 1 @UnitId = UnitId, @MinId = MinId, @MaxId = MaxId FROM @Ranges ORDER BY UnitId
--  INSERT INTO @Resources
--    EXECUTE dbo.GetResourcesByTypeAndSurrogateIdRange 96, @MinId, @MaxId, NULL, @MaxId -- last is to invoke snapshot logic 
--  DELETE FROM @Resources
--  DELETE FROM @Ranges WHERE UnitId = @UnitId
--END
