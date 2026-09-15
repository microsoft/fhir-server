--DROP TYPE dbo.VectorSearchParamList
GO
CREATE TYPE dbo.VectorSearchParamList AS TABLE
(
    ResourceTypeId           smallint      NOT NULL
   ,ResourceSurrogateId      bigint        NOT NULL
   ,SearchParamId            smallint      NOT NULL
   ,ChunkOrdinal             smallint      NOT NULL
   ,EmbeddingModelId         smallint      NOT NULL
   ,SourceTextHash           binary(32)    NOT NULL
   ,SourceTextCompressed     varbinary(max) NOT NULL
   ,Embedding                nvarchar(max) NOT NULL

    UNIQUE (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal)
)
GO
