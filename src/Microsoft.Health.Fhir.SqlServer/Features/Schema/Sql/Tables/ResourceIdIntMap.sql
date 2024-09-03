CREATE TABLE dbo.ResourceIdIntMap
(
    ResourceTypeId  smallint    NOT NULL
   ,ResourceId      varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
   ,ResourceIdInt   bigint      IDENTITY(1, 1) 
    
    CONSTRAINT PKC_ResourceIdIntMap_ResourceIdInt_ResourceTypeId PRIMARY KEY CLUSTERED (ResourceIdInt, ResourceTypeId) WITH (DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
   ,CONSTRAINT U_ResourceIdIntMap_ResourceId_ResourceTypeId UNIQUE (ResourceId, ResourceTypeId) WITH (IGNORE_DUP_KEY = ON, DATA_COMPRESSION = PAGE) ON PartitionScheme_ResourceTypeId (ResourceTypeId)
)
