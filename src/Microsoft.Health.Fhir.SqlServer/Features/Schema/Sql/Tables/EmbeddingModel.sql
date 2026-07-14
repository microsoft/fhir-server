
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
