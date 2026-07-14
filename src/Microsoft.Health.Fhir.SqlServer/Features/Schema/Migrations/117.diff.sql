/*************************************************************
    Semantic search feature
    Adds the EmbeddingModel registry table.

    NOTE: VectorSearchParam (with a VECTOR(1536) column) is intentionally
    not part of this migration or the canonical schema yet. The build-time
    model generator in Microsoft.Health.SqlServer has no column type for the
    SQL VECTOR type and emits the abstract base Column, which does not
    compile. Once the shared library adds vector column support, the vector
    table can be brought into the canonical schema.
**************************************************************/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EmbeddingModel')
BEGIN
    CREATE TABLE dbo.EmbeddingModel
    (
        EmbeddingModelId        smallint        IDENTITY(1,1)   NOT NULL,
        CONSTRAINT UQ_EmbeddingModel_EmbeddingModelId UNIQUE (EmbeddingModelId),
        ModelName               varchar(128)    COLLATE Latin1_General_100_CS_AS NOT NULL,
        Dimensions              int             NOT NULL,
        Endpoint                varchar(512)    COLLATE Latin1_General_100_CS_AS NOT NULL,
        ChunkSize               int             NOT NULL,
        ChunkOverlap            int             NOT NULL,
        CONSTRAINT PKC_EmbeddingModel PRIMARY KEY CLUSTERED (ModelName)
        WITH (DATA_COMPRESSION = PAGE)
    )
END
GO
