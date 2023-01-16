--DROP TYPE dbo.ReferenceSearchParamList
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ReferenceSearchParamList')
CREATE TYPE dbo.ReferenceSearchParamList AS TABLE
(
    ResourceTypeId smallint NOT NULL
   ,ResourceRecordId bigint NOT NULL
   ,SearchParamId smallint NOT NULL
   ,BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL
   ,ReferenceResourceTypeId smallint NULL
   ,ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ReferenceResourceVersion int NULL

   UNIQUE (ResourceTypeId, ResourceRecordId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId) 
)
GO
--DROP TYPE dbo.ResourceIdForChangesList
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceIdForChangesList')
CREATE TYPE dbo.ResourceIdForChangesList AS TABLE
(
    ResourceTypeId     smallint            NOT NULL
   ,ResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
   ,Version            int                 NOT NULL
   ,IsDeleted          bit                 NOT NULL

    PRIMARY KEY (ResourceTypeId, ResourceId)
)
GO
--DROP TYPE dbo.ResourceList
GO
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'ResourceList')
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
CREATE OR ALTER PROCEDURE dbo.CaptureResourceIdsForChanges @Ids dbo.ResourceIdForChangesList READONLY
AS
set nocount on
-- This procedure is intended to be called from the MergeResources procedure and relies on its transaction logic
INSERT INTO dbo.ResourceChangeData 
       ( ResourceId, ResourceTypeId, ResourceVersion,                                              ResourceChangeTypeId )
  SELECT ResourceId, ResourceTypeId,         Version, CASE WHEN IsDeleted = 1 THEN 2 WHEN Version > 1 THEN 1 ELSE 0 END
    FROM @Ids
GO
