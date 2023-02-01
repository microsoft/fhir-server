GO
CREATE OR ALTER PROCEDURE dbo.GetResourcesByTypeAndSurrogateIdRange
@ResourceTypeId SMALLINT, @StartId BIGINT, @EndId BIGINT, @GlobalStartId BIGINT=NULL, @GlobalEndId BIGINT=NULL
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetResourcesByTypeAndSurrogateIdRange', @Mode AS VARCHAR (100) = 'RT=' + isnull(CONVERT (VARCHAR, @ResourceTypeId), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @StartId), 'NULL') + ' E=' + isnull(CONVERT (VARCHAR, @EndId), 'NULL') + ' GS=' + isnull(CONVERT (VARCHAR, @GlobalStartId), 'NULL') + ' GE=' + isnull(CONVERT (VARCHAR, @GlobalEndId), 'NULL'), @st AS DATETIME = getUTCdate();
BEGIN TRY
    DECLARE @ResourceIds TABLE (
        ResourceId          VARCHAR (64) COLLATE Latin1_General_100_CS_AS,
        ResourceSurrogateId BIGINT      ,
        RowId               INT         ,
        PRIMARY KEY (ResourceId, RowId));
    IF @GlobalStartId IS NULL
        SET @GlobalStartId = 0;
    IF @GlobalEndId IS NOT NULL
        INSERT INTO @ResourceIds
        SELECT ResourceId,
               ResourceSurrogateId,
               row_number() OVER (PARTITION BY ResourceId ORDER BY ResourceSurrogateId DESC) AS RowId
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceId IN (SELECT DISTINCT ResourceId 
                                  FROM   dbo.Resource
                                  WHERE  ResourceTypeId = @ResourceTypeId
                                         AND ResourceSurrogateId BETWEEN @StartId AND @EndId
                                         AND IsHistory = 1
                                         AND IsDeleted = 0)
               AND ResourceSurrogateId BETWEEN @GlobalStartId AND @GlobalEndId;
    IF EXISTS (SELECT *
               FROM   @ResourceIds)
        BEGIN
            DECLARE @SurrogateIdMap TABLE (
                MaxSurrogateId BIGINT PRIMARY KEY);
            INSERT INTO @SurrogateIdMap
            SELECT A.ResourceSurrogateId AS MaxSurrogateId
            FROM   (SELECT *
                    FROM   @ResourceIds
                    WHERE  RowId = 1
                           AND ResourceSurrogateId BETWEEN @StartId AND @EndId) AS A;
            SELECT @ResourceTypeId,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.ResourceId ELSE A.ResourceId END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.Version ELSE A.Version END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsDeleted ELSE A.IsDeleted END,
                   isnull(C.ResourceSurrogateId, A.ResourceSurrogateId),
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RequestMethod ELSE A.RequestMethod END,
                   CONVERT (BIT, 1) AS IsMatch,
                   CONVERT (BIT, 0) AS IsPartial,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.IsRawResourceMetaSet ELSE A.IsRawResourceMetaSet END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.SearchParamHash ELSE A.SearchParamHash END,
                   CASE WHEN C.ResourceSurrogateId IS NOT NULL THEN C.RawResource ELSE A.RawResource END
            FROM   dbo.Resource AS A
                   LEFT OUTER JOIN
                   @SurrogateIdMap AS B
                   ON B.MaxSurrogateId = A.ResourceSurrogateId
                   LEFT OUTER JOIN
                   dbo.Resource AS C
                   ON C.ResourceTypeId = @ResourceTypeId
                      AND C.ResourceSurrogateId = MaxSurrogateId
            WHERE  A.ResourceTypeId = @ResourceTypeId
                   AND A.ResourceSurrogateId BETWEEN @StartId AND @EndId
                   AND (A.IsHistory = 0
                        OR MaxSurrogateId IS NOT NULL)
                   AND A.IsDeleted = 0;
        END
    ELSE
        SELECT ResourceTypeId,
               ResourceId,
               Version,
               IsDeleted,
               ResourceSurrogateId,
               RequestMethod,
               CONVERT (BIT, 1) AS IsMatch,
               CONVERT (BIT, 0) AS IsPartial,
               IsRawResourceMetaSet,
               SearchParamHash,
               RawResource
        FROM   dbo.Resource
        WHERE  ResourceTypeId = @ResourceTypeId
               AND ResourceSurrogateId BETWEEN @StartId AND @EndId
               AND IsHistory = 0
               AND IsDeleted = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
GO
