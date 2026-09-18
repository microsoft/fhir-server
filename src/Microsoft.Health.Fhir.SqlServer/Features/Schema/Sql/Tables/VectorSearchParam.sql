CREATE TABLE dbo.VectorSearchParam
(
    ResourceTypeId          smallint        NOT NULL,
    ResourceSurrogateId     bigint          NOT NULL,
    SearchParamId           smallint        NOT NULL,
    ChunkOrdinal            smallint        NOT NULL
        CONSTRAINT DF_VectorSearchParam_ChunkOrdinal DEFAULT 0,
    EmbeddingModelId        smallint        NOT NULL,
    SourceTextHash          binary(32)      NOT NULL,
    SourceTextCompressed    varbinary(max)  NOT NULL,
    Embedding               vector(1536)    NOT NULL
)

ALTER TABLE dbo.VectorSearchParam SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE dbo.VectorSearchParam ADD CONSTRAINT PKC_VectorSearchParam_ResourceTypeId_ResourceSurrogateId_SearchParamId_ChunkOrdinal
PRIMARY KEY CLUSTERED
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId,
    ChunkOrdinal
)
WITH (DATA_COMPRESSION = PAGE)