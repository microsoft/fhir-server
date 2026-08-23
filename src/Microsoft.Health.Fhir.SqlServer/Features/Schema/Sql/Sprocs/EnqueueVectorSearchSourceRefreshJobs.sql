CREATE PROCEDURE dbo.EnqueueVectorSearchSourceRefreshJobs
    @Resources dbo.ResourceList READONLY
AS
SET NOCOUNT ON

DECLARE @Definitions dbo.StringList

INSERT INTO @Definitions (String)
SELECT DISTINCT
       (
           SELECT 11 AS TypeId,
                  RT.Name AS SourceResourceType,
                  A.ResourceId AS SourceResourceId,
                  CONVERT(varchar(64), A.Version) AS SourceResourceVersion
           FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
       )
FROM @Resources A
JOIN dbo.ResourceType RT ON RT.ResourceTypeId = A.ResourceTypeId
WHERE A.IsHistory = 0
  AND EXISTS
      (
          SELECT 1
          FROM dbo.VectorSearchParam V
          JOIN dbo.Resource R
            ON R.ResourceTypeId = V.ResourceTypeId
           AND R.ResourceSurrogateId = V.ResourceSurrogateId
           AND R.IsHistory = 0
           AND R.IsDeleted = 0
          WHERE V.SourceResourceTypeId = A.ResourceTypeId
            AND V.SourceResourceId = A.ResourceId
            AND (V.ResourceTypeId <> A.ResourceTypeId OR R.ResourceId <> A.ResourceId)
      )

IF EXISTS (SELECT 1 FROM @Definitions)
    EXECUTE dbo.EnqueueJobs
        @QueueType = 6,
        @Definitions = @Definitions,
        @ForceOneActiveJobGroup = 0,
        @ReturnJobs = 0
GO
