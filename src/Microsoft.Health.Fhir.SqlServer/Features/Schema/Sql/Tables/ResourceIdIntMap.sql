CREATE TABLE dbo.ResourceIdIntMap
(
    ResourceTypeId  smallint    NOT NULL
   ,ResourceIdInt   bigint      NOT NULL
   ,ResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
    
    CONSTRAINT PKC_ResourceIdIntMap_ResourceIdInt_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceIdInt, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceIdIntMap_ResourceId_ResourceTypeId UNIQUE (ResourceId, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)

ALTER TABLE dbo.ResourceIdIntMap SET ( LOCK_ESCALATION = AUTO )
