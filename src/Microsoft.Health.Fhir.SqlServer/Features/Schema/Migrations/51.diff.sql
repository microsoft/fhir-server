GO
CREATE OR ALTER PROCEDURE  dbo.QueryIdentifierWithIncludeAndOtherParam
      @p0 SmallInt,
      @p1 SmallInt,
      @p2 SmallInt,
      @p3 SmallInt,
      @p4 Int,
      @p5 VarChar(256),
      @p6 SmallInt,
      @p7 VarChar(256),
      @p8 Int,
      @p9 SmallInt,
      @p10 Int
AS
WITH cte0 AS
(
    SELECT refSource.ResourceTypeId AS T1, refSource.ResourceSurrogateId AS Sid1, refTarget.ResourceTypeId AS T2, refTarget.ResourceSurrogateId AS Sid2 
    FROM dbo.ReferenceSearchParam refSource
    INNER JOIN dbo.Resource refTarget
    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId
        AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = @p0
        AND refTarget.IsHistory = 0
        AND refSource.IsHistory = 0
        AND refSource.ResourceTypeId IN (@p1)
        AND refSource.ReferenceResourceTypeId IN (@p2)
        AND refSource.ResourceTypeId = @p1
),
cte1 AS
(
    SELECT T1, Sid1, ResourceTypeId AS T2, 
    ResourceSurrogateId AS Sid2
    FROM dbo.TokenSearchParam
    INNER JOIN cte0
    ON ResourceTypeId = T2
        AND ResourceSurrogateId = Sid2
    WHERE IsHistory = 0
        AND SearchParamId = @p3
        AND SystemId = @p4
        AND Code = @p5
    
),
cte2 AS
(
    SELECT refSource.ResourceTypeId AS T1, refSource.ResourceSurrogateId AS Sid1, refTarget.ResourceTypeId AS T2, refTarget.ResourceSurrogateId AS Sid2 
    FROM dbo.ReferenceSearchParam refSource
    INNER JOIN dbo.Resource refTarget
    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId
        AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = @p0
        AND refTarget.IsHistory = 0
        AND refSource.IsHistory = 0
        AND refSource.ResourceTypeId IN (@p1)
        AND refSource.ReferenceResourceTypeId IN (@p2)
        AND EXISTS(SELECT * FROM cte1 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1)
        AND refSource.ResourceTypeId = @p1
),
cte3 AS
(
    SELECT T1, Sid1, ResourceTypeId AS T2, 
    ResourceSurrogateId AS Sid2
    FROM dbo.TokenSearchParam
    INNER JOIN cte2
    ON ResourceTypeId = T2
        AND ResourceSurrogateId = Sid2
    WHERE IsHistory = 0
        AND SearchParamId = @p6
        AND Code = @p7
),
cte4 AS
(
    SELECT ROW_NUMBER() OVER(ORDER BY T1 ASC, Sid1 ASC) AS Row, *
    FROM
    (
        SELECT DISTINCT TOP (@p8) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial 
        FROM cte3
        ORDER BY T1 ASC, Sid1 ASC
    ) t
),
cte5 AS
(
    SELECT DISTINCT refTarget.ResourceTypeId AS T1, refTarget.ResourceSurrogateId AS Sid1, 0 AS IsMatch 
    FROM dbo.ReferenceSearchParam refSource
    INNER JOIN dbo.Resource refTarget
    ON refSource.ReferenceResourceTypeId = refTarget.ResourceTypeId
        AND refSource.ReferenceResourceId = refTarget.ResourceId
    WHERE refSource.SearchParamId = @p9
        AND refTarget.IsHistory = 0
        AND refSource.IsHistory = 0
        AND refTarget.IsDeleted = 0
        AND refSource.ResourceTypeId IN (101)
        AND EXISTS( SELECT * FROM cte4 WHERE refSource.ResourceTypeId = T1 AND refSource.ResourceSurrogateId = Sid1 AND Row < @p10)
),
cte6 AS
(
    SELECT DISTINCT T1, Sid1, IsMatch, 0 AS IsPartial 
    FROM cte5
),
cte7 AS
(
    SELECT T1, Sid1, IsMatch, IsPartial 
    FROM cte4
    UNION ALL
    SELECT T1, Sid1, IsMatch, IsPartial
    FROM cte6 WHERE NOT EXISTS (SELECT * FROM cte4 WHERE cte4.Sid1 = cte6.Sid1 AND cte4.T1 = cte6.T1)
)
SELECT DISTINCT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId, r.RequestMethod, CAST(IsMatch AS bit) AS IsMatch, CAST(IsPartial AS bit) AS IsPartial, r.IsRawResourceMetaSet, r.SearchParamHash, r.RawResource
FROM dbo.Resource r

INNER JOIN cte7
ON r.ResourceTypeId = cte7.T1 AND 
r.ResourceSurrogateId = cte7.Sid1
ORDER BY IsMatch DESC, r.ResourceTypeId ASC, r.ResourceSurrogateId ASC
OPTION (OPTIMIZE FOR UNKNOWN)
GO
