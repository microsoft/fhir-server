-- Standalone demo: proves vector similarity search works end to end.
-- Inserts three chunk vectors and ranks them against a query vector.
SET NOCOUNT ON;

-- Clean any prior demo rows so this is repeatable.
DELETE FROM dbo.VectorSearchParam WHERE ResourceTypeId = 1 AND ResourceSurrogateId = 1 AND SearchParamId = 1;
DELETE FROM dbo.EmbeddingModel WHERE ModelName = 'demo-model';

INSERT INTO dbo.EmbeddingModel (ModelName, Dimensions, Endpoint, ChunkSize, ChunkOverlap)
VALUES ('demo-model', 1536, 'https://example.local', 512, 64);
DECLARE @m smallint = CAST(SCOPE_IDENTITY() AS smallint);

INSERT INTO dbo.VectorSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, ChunkOrdinal, EmbeddingModelId, SourceTextHash, Embedding)
VALUES
 (1, 1, 1, 0, @m, 0x00, CAST('[1,'    + REPLICATE('0,',1534) + '0]' AS VECTOR(1536))),
 (1, 1, 1, 1, @m, 0x00, CAST('[0,1,'  + REPLICATE('0,',1533) + '0]' AS VECTOR(1536))),
 (1, 1, 1, 2, @m, 0x00, CAST('[0,0,1,'+ REPLICATE('0,',1532) + '0]' AS VECTOR(1536)));

DECLARE @q VECTOR(1536) = CAST('[0.1,0.9,' + REPLICATE('0,',1533) + '0]' AS VECTOR(1536));
SELECT ChunkOrdinal,
       VECTOR_DISTANCE('cosine', Embedding, @q)          AS Distance,
       1 - VECTOR_DISTANCE('cosine', Embedding, @q) / 2  AS Score
FROM dbo.VectorSearchParam
WHERE ResourceTypeId = 1 AND ResourceSurrogateId = 1 AND SearchParamId = 1
ORDER BY Distance;
