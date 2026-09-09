CREATE PROCEDURE dbo.GetVectorSearchSourceDependencies
    @SourceResourceTypeId smallint,
    @SourceResourceId varchar(64)
AS
SET NOCOUNT ON

SELECT DISTINCT V.ResourceTypeId, R.ResourceId
FROM dbo.VectorSearchParam V
JOIN dbo.Resource R
  ON R.ResourceTypeId = V.ResourceTypeId
 AND R.ResourceSurrogateId = V.ResourceSurrogateId
 AND R.IsHistory = 0
 AND R.IsDeleted = 0
WHERE V.SourceResourceTypeId = @SourceResourceTypeId
  AND V.SourceResourceId = @SourceResourceId
  AND (V.ResourceTypeId <> @SourceResourceTypeId OR R.ResourceId <> @SourceResourceId)
GO
