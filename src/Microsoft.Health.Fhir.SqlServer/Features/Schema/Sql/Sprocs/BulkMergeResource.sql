/*************************************************************
    Stored procedures for bulk merge resources
**************************************************************/
--
-- STORED PROCEDURE
--     BulkMergeResource
--
-- DESCRIPTION
--     Stored procedures for bulk merge resource
--
-- PARAMETERS
--     @resources
--         * input resources
CREATE PROCEDURE dbo.BulkMergeResource
    @resources dbo.BulkImportResourceType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    BEGIN TRANSACTION

    MERGE INTO [dbo].[Resource] WITH (ROWLOCK, INDEX(IX_Resource_ResourceTypeId_ResourceId_Version)) AS target
    USING @resources AS source
    ON source.[ResourceTypeId] = target.[ResourceTypeId]
        AND source.[ResourceId] = target.[ResourceId]
        AND source.[Version] = target.[Version]
    WHEN NOT MATCHED BY target THEN
    INSERT ([ResourceTypeId]
            , [ResourceId]
            , [Version]
            , [IsHistory]
            , [ResourceSurrogateId]
            , [IsDeleted]
            , [RequestMethod]
            , [RawResource]
            , [IsRawResourceMetaSet]
            , [SearchParamHash])
    VALUES ([ResourceTypeId]
            , [ResourceId]
            , [Version]
            , [IsHistory]
            , [ResourceSurrogateId]
            , [IsDeleted]
            , [RequestMethod]
            , [RawResource]
            , [IsRawResourceMetaSet]
            , [SearchParamHash])
    OUTPUT Inserted.[ResourceSurrogateId];

    COMMIT TRANSACTION
GO
