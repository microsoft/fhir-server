CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId                      smallint                NOT NULL,
    ResourceSurrogateId                 bigint                  NOT NULL,
    SearchParamId                       smallint                NOT NULL,
    BaseUri                             varchar(128)            COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId             smallint                NULL,
    ReferenceResourceId                 varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion            int                     NULL,
    IsHistory                           bit                     NOT NULL,
)

ALTER TABLE dbo.ReferenceSearchParam ADD CONSTRAINT DF_ReferenceSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.ReferenceSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE INDEX IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId ON dbo.ReferenceSearchParam 
  ( 
    ReferenceResourceId
   ,ReferenceResourceTypeId
   ,SearchParamId
   ,BaseUri
   ,ResourceSurrogateId
   ,ResourceTypeId
  )
  WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

