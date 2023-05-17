--DROP TYPE dbo.ResourceDateKeyList
GO
CREATE TYPE dbo.ResourceDateKeyList AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL

    PRIMARY KEY (ResourceTypeId, ResourceId, ResourceSurrogateId)
)
GO
