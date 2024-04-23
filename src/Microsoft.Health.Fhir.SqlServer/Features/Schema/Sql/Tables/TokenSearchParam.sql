﻿CREATE TABLE dbo.TokenSearchParam
(
    ResourceTypeId              smallint                NOT NULL,
    ResourceSurrogateId         bigint                  NOT NULL,
    SearchParamId               smallint                NOT NULL,
    SystemId                    int                     NULL,
    Code                        varchar(256)            COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow                varchar(max)            COLLATE Latin1_General_100_CS_AS NULL
)

ALTER TABLE dbo.TokenSearchParam ADD CONSTRAINT CHK_TokenSearchParam_CodeOverflow CHECK (LEN(Code) = 256 OR CodeOverflow IS NULL)

ALTER TABLE dbo.TokenSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code_INCLUDE_SystemId 
ON dbo.TokenSearchParam 
(
    SearchParamId, 
    Code
) 
INCLUDE 
(
    SystemId
) 
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId);

