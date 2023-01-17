CREATE PROCEDURE dbo.CaptureResourceIdsForChanges @Ids dbo.ResourceIdForChangesList READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM @Ids
GO
