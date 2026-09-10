CREATE PROCEDURE dbo.HardDeleteResourceWithVectorSearchSourceRefresh
    @ResourceTypeId smallint,
    @ResourceId varchar(64),
    @KeepCurrentVersion bit,
    @IsResourceChangeCaptureEnabled bit
AS
SET NOCOUNT ON
DECLARE @InitialTranCount int = @@TRANCOUNT
DECLARE @ChangedSources dbo.ResourceList

BEGIN TRY
  IF @InitialTranCount = 0 BEGIN TRANSACTION

  IF @KeepCurrentVersion = 0
    INSERT INTO @ChangedSources
        (ResourceTypeId, ResourceSurrogateId, ResourceId, Version, HasVersionToCompare, IsDeleted, IsHistory, KeepHistory, RawResource, IsRawResourceMetaSet, RequestMethod, SearchParamHash)
    SELECT TOP (1)
        ResourceTypeId, ResourceSurrogateId, ResourceId, Version + 1, 0, 1, 0, 0, 0x0, 0, NULL, NULL
    FROM dbo.Resource
    WHERE ResourceTypeId = @ResourceTypeId
      AND ResourceId = @ResourceId
      AND IsHistory = 0
    ORDER BY ResourceSurrogateId DESC

  EXECUTE dbo.HardDeleteResource
      @ResourceTypeId = @ResourceTypeId,
      @ResourceId = @ResourceId,
      @KeepCurrentVersion = @KeepCurrentVersion,
      @IsResourceChangeCaptureEnabled = @IsResourceChangeCaptureEnabled

  EXECUTE dbo.EnqueueVectorSearchSourceRefreshJobs @Resources = @ChangedSources

  IF @InitialTranCount = 0 COMMIT TRANSACTION
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@TRANCOUNT > 0 ROLLBACK TRANSACTION
  THROW
END CATCH
GO
