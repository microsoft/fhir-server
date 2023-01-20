--DROP TYPE dbo.ResourceKeyList
GO
CREATE TYPE dbo.ResourceKeyList AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL

    PRIMARY KEY (ResourceTypeId, ResourceId)
)
GO
