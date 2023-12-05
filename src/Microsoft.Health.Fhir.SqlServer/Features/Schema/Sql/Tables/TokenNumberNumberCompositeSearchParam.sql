CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2 decimal(36,18) NULL,
    LowValue2 decimal(36,18) NULL,
    HighValue2 decimal(36,18) NULL,
    SingleValue3 decimal(36,18) NULL,
    LowValue3 decimal(36,18) NULL,
    HighValue3 decimal(36,18) NULL,
    HasRange bit NOT NULL,
    IsHistory bit NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
)

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam ADD CONSTRAINT DF_TokenNumberNumberCompositeSearchParam_IsHistory DEFAULT 0 FOR IsHistory

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam ADD CONSTRAINT CHK_TokenNumberNumberCompositeSearchParam_CodeOverflow1 CHECK (LEN(Code1) = 256 OR CodeOverflow1 IS NULL)

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code1_SingleValue2_SingleValue3_INCLUDE_SystemId1_WHERE_HasRange_0
ON dbo.TokenNumberNumberCompositeSearchParam
(
    SearchParamId,
    Code1,
    SingleValue2,
    SingleValue3 -- TODO: Do we need this as key column?
)
INCLUDE
(
    SystemId1
)
WHERE HasRange = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3_INCLUDE_SystemId1_WHERE_HasRange_1
ON dbo.TokenNumberNumberCompositeSearchParam
(
    SearchParamId,
    Code1,
    LowValue2, 
    HighValue2, -- TODO: Do we need this as key column?
    LowValue3, -- TODO: Do we need this as key column?
    HighValue3 -- TODO: Do we need this as key column?
)
INCLUDE
(
    SystemId1
)
WHERE HasRange = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId (ResourceTypeId)

