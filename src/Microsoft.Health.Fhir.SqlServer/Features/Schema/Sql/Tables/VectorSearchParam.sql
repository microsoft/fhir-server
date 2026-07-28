CREATE TABLE dbo.VectorSearchParam
(
    ResourceTypeId          smallint        NOT NULL,
    ResourceSurrogateId     bigint          NOT NULL,
    SearchParamId           smallint        NOT NULL,
    ChunkOrdinal            smallint        NOT NULL
        CONSTRAINT DF_VectorSearchParam_ChunkOrdinal DEFAULT 0,
    EmbeddingModelId        smallint        NOT NULL,
    ChunkText               nvarchar(max)   NOT NULL,
    SourceTextHash          binary(32)      NOT NULL,
    SourceResourceTypeId    smallint        NULL,
    SourceResourceId        varchar(64)     COLLATE Latin1_General_100_CS_AS NULL,
    SourceResourceVersion   varchar(64)     COLLATE Latin1_General_100_CS_AS NULL,
    SourcePath              nvarchar(512)   NULL,
    Embedding               vector(1536)    NOT NULL
)

ALTER TABLE dbo.VectorSearchParam SET ( LOCK_ESCALATION = AUTO )

ALTER TABLE dbo.VectorSearchParam ADD CONSTRAINT PKC_VectorSearchParam
PRIMARY KEY CLUSTERED
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId,
    ChunkOrdinal
)
WITH (DATA_COMPRESSION = PAGE)