CREATE OR ALTER PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange @ResourceTypeId smallint, @StartId bigint, @EndId bigint, @GlobalStartId bigint = NULL, @GlobalEndId bigint = NULL, @IncludeHistory bit = 0, @IncludeDeleted bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourcesByTypeAndSurrogateIdRange'
       ,@Mode varchar(100) = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')
                           +' S='+isnull(convert(varchar,@StartId),'NULL')
                           +' E='+isnull(convert(varchar,@EndId),'NULL')
                           +' GS='+isnull(convert(varchar,@GlobalStartId),'NULL') -- Is global start id needed? I'm not seeing a usecase for setting it.
                           +' GE='+isnull(convert(varchar,@GlobalEndId),'NULL') -- Could this just be a boolean for if historical records should be returned? GlobalEndId should equal EndId in all cases I can think of.
                           +' HI='+isnull(convert(varchar,@IncludeHistory),'NULL')
                           +' DE'+isnull(convert(varchar,@IncludeDeleted),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  DECLARE @ResourceIds TABLE (ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS, ResourceSurrogateId bigint, RowId int, PRIMARY KEY (ResourceId, RowId))

  IF @GlobalStartId IS NULL -- export from time zero (no lower boundary)
    SET @GlobalStartId = 0

  IF @GlobalEndId IS NOT NULL -- snapshot view
    INSERT INTO @ResourceIds
      SELECT ResourceId, ResourceSurrogateId, RowId = row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId DESC)
        FROM dbo.Resource 
        WHERE ResourceTypeId = @ResourceTypeId
          AND ResourceId IN (SELECT DISTINCT ResourceId
                               FROM dbo.Resource 
                               WHERE ResourceTypeId = @ResourceTypeId 
                                 AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
                                 AND IsHistory = 1
                                 AND (IsDeleted = 0 OR @IncludeDeleted = 1) -- TBD if this is needed. 
                            )
          AND ResourceSurrogateId BETWEEN @GlobalStartId AND @GlobalEndId
   
  IF EXISTS (SELECT * FROM @ResourceIds)
  BEGIN
    DECLARE @SurrogateIdMap TABLE (MaxSurrogateId bigint PRIMARY KEY)
    INSERT INTO @SurrogateIdMap
      SELECT MaxSurrogateId = A.ResourceSurrogateId
        FROM (SELECT * FROM @ResourceIds WHERE RowId = 1 AND ResourceSurrogateId BETWEEN @StartId AND @EndId) A

    SELECT @ResourceTypeId
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.ResourceId ELSE A.ResourceId END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.Version ELSE A.Version END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsDeleted ELSE A.IsDeleted END
          ,isnull(C.ResourceSurrogateId, A.ResourceSurrogateId)
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RequestMethod ELSE A.RequestMethod END
          ,IsMatch = convert(bit,1)
          ,IsPartial = convert(bit,0)
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsRawResourceMetaSet ELSE A.IsRawResourceMetaSet END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.SearchParamHash ELSE A.SearchParamHash END
          ,CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RawResource ELSE A.RawResource END
      FROM dbo.Resource A
           LEFT OUTER JOIN @SurrogateIdMap B ON B.MaxSurrogateId = A.ResourceSurrogateId
           LEFT OUTER JOIN dbo.Resource C ON C.ResourceTypeId = @ResourceTypeId AND C.ResourceSurrogateId = MaxSurrogateId
      WHERE A.ResourceTypeId = @ResourceTypeId 
        AND A.ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (A.IsHistory = 0 OR MaxSurrogateId IS NOT NULL OR @IncludeHistory = 1)
        AND (A.IsDeleted = 0 OR @IncludeDeleted = 1)
  END
  ELSE
    SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId, RequestMethod, IsMatch = convert(bit,1), IsPartial = convert(bit,0), IsRawResourceMetaSet, SearchParamHash, RawResource 
      FROM dbo.Resource 
      WHERE ResourceTypeId = @ResourceTypeId 
        AND ResourceSurrogateId BETWEEN @StartId AND @EndId 
        AND (IsHistory = 0 OR @IncludeHistory = 1)
        AND (IsDeleted = 0 OR @IncludeDeleted = 1)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
