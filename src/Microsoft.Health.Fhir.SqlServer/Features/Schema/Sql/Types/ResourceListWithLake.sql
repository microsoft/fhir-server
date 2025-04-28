﻿--DROP TYPE dbo.ResourceListWithLake
GO
CREATE TYPE dbo.ResourceListWithLake AS TABLE
(
    ResourceTypeId       smallint            NOT NULL
   ,ResourceSurrogateId  bigint              NOT NULL
   ,ResourceId           varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version              int                 NOT NULL
   ,HasVersionToCompare  bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
   ,IsDeleted            bit                 NOT NULL
   ,IsHistory            bit                 NOT NULL
   ,KeepHistory          bit                 NOT NULL
   ,RawResource          varbinary(max)      NULL
   ,IsRawResourceMetaSet bit                 NOT NULL
   ,RequestMethod        varchar(10)         NULL
   ,SearchParamHash      varchar(64)         NULL
   ,FileId               bigint              NULL
   ,OffsetInFile         int                 NULL
   ,ResourceLength       int                 NULL

    PRIMARY KEY (ResourceTypeId, ResourceSurrogateId)
   ,UNIQUE (ResourceTypeId, ResourceId, Version)
)
GO
