--DROP TYPE dbo.ResourceList
GO
CREATE TYPE dbo.ResourceList AS TABLE
(
    Offset             bigint              NOT NULL
   ,ResourceTypeId     smallint            NOT NULL
   ,ResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,IsDeleted          bit                 NOT NULL
   ,RawResource        varbinary(max)      NOT NULL
   ,RequestMethod      varchar(10)         NULL
   ,SearchParamHash    varchar(64)         NULL
   ,IsHistory          bit                 NOT NULL
   ,ComparedVersion    int                 NULL

    PRIMARY KEY (Offset)
   ,UNIQUE (ResourceTypeId, ResourceId)
)
GO
