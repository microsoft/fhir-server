--DROP TYPE dbo.VectorSearchParamList
GO
CREATE TYPE dbo.VectorSearchParamList AS TABLE
(
    ResourceTypeId           smallint      NOT NULL
   ,ResourceSurrogateId      bigint        NOT NULL
   ,SearchParamId            smallint      NOT NULL
   ,ChunkOrdinal             smallint      NOT NULL
   ,EmbeddingModelId         smallint      NOT NULL
   ,ChunkText                nvarchar(max) NOT NULL
   ,SourceTextHash           binary(32)    NOT NULL
    ,SourceResourceTypeId     smallint      NOT NULL
    ,SourceResourceId         varchar(64)   COLLATE Latin1_General_100_CS_AS NOT NULL
    ,SourceResourceVersion    varchar(64)   COLLATE Latin1_General_100_CS_AS NULL
    ,SourcePath               nvarchar(512) NOT NULL
   ,Embedding                nvarchar(max) NOT NULL
)
GO
