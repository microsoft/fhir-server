CREATE PROCEDURE dbo.CaptureResourceIdsForChanges @Resources dbo.ResourceList READONLY, @ResourcesLake dbo.ResourceListWithLake READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM (SELECT ResourceId, ResourceTypeId, Version, IsHistory, IsDeleted FROM @Resources UNION ALL SELECT ResourceId, ResourceTypeId, Version, IsHistory, IsDeleted FROM @ResourcesLake) A
    WHERE IsHistory = 0
GO
