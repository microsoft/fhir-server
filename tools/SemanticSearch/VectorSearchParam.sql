-- -------------------------------------------------------------------------------------------------
-- Standalone creation script for the VectorSearchParam table.
--
-- This table is NOT part of the build-time canonical schema yet, because the
-- Microsoft.Health.SqlServer model generator has no column type for the SQL
-- VECTOR type (it emits the abstract base Column, which does not compile).
--
-- Run this manually against a VECTOR-capable database (Azure SQL DB or
-- SQL Server 2025) to create the table for local testing and demos, e.g.:
--
--   sqlcmd -S "tcp:fhir-vec-annag.database.windows.net,1433" -d "FHIR" `
--     -G -U "t-annag@microsoft.com" -C -i tools/SemanticSearch/VectorSearchParam.sql
-- -------------------------------------------------------------------------------------------------

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VectorSearchParam')
BEGIN
    CREATE TABLE dbo.VectorSearchParam
    (
        ResourceTypeId          smallint        NOT NULL,
        ResourceSurrogateId     bigint          NOT NULL,
        SearchParamId           smallint        NOT NULL,
        ChunkOrdinal            int             NOT NULL,
        EmbeddingModelId        smallint        NOT NULL,
        SourceTextHash          binary(32)      NOT NULL,
        Embedding               vector(1536)    NOT NULL
    )

    ALTER TABLE dbo.VectorSearchParam SET ( LOCK_ESCALATION = AUTO )

    CREATE CLUSTERED INDEX IXC_VectorSearchParam
    ON dbo.VectorSearchParam
    (
        ResourceTypeId,
        ResourceSurrogateId,
        SearchParamId,
        ChunkOrdinal
    )
    WITH (DATA_COMPRESSION = PAGE)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
