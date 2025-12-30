CREATE PROCEDURE dbo.CaptureResourceIdsForChanges 
    @Resources dbo.ResourceList READONLY,
    @Resources_Temp dbo.ResourceList_Temp READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic

IF EXISTS (SELECT 1 FROM @Resources_Temp)
BEGIN
    INSERT INTO dbo.ResourceChangeData 
           ( ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId )
      SELECT ResourceId, ResourceTypeId, Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
        FROM @Resources_Temp
        WHERE IsHistory = 0
END
ELSE
BEGIN
    INSERT INTO dbo.ResourceChangeData 
           ( ResourceId, ResourceTypeId, ResourceVersion, ResourceChangeTypeId )
      SELECT ResourceId, ResourceTypeId, Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
        FROM @Resources
        WHERE IsHistory = 0
END
GO
