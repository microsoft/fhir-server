--DROP TYPE dbo.ResourceList
GO
CREATE TYPE dbo.ResourceList AS TABLE
(
    ResourceTypeId      smallint            NOT NULL
   ,ResourceRecordId    bigint              NOT NULL -- this can be offset in a batch or a surrogate id
   ,ResourceId          varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version             int                 NOT NULL
   ,HasVersionToCompare bit                 NOT NULL -- in case of multiple versions per resource indicates that row contains (existing version + 1) value
   ,IsDeleted           bit                 NOT NULL
   ,IsHistory           bit                 NOT NULL
   ,RawResource         varbinary(max)      NOT NULL
   ,RequestMethod       varchar(10)         NULL
   ,SearchParamHash     varchar(64)         NULL

    PRIMARY KEY (ResourceTypeId, ResourceRecordId)
   ,UNIQUE (ResourceTypeId, ResourceId)
)
GO
