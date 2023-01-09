--DROP TYPE dbo.ResourceIdForChangesList
GO
CREATE TYPE dbo.ResourceIdForChangesList AS TABLE
(
    ResourceTypeId     smallint            NOT NULL
   ,ResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version            int                 NOT NULL
   ,IsDeleted          bit                 NOT NULL

    PRIMARY KEY (ResourceTypeId, ResourceId)
)
GO
