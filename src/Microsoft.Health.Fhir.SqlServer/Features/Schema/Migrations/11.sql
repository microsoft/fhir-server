-- Style guide: please see: https://github.com/ktaranov/sqlserver-kit/blob/master/SQL%20Server%20Name%20Convention%20and%20T-SQL%20Programming%20Style.md

/*************************************************************
    Schema Version
**************************************************************/

INSERT INTO dbo.SchemaVersion
VALUES
    (11, 'started')

GO

/*************************************************************
    Migration progress
**************************************************************/

CREATE TABLE dbo.SchemaMigrationProgress
(
    Timestamp datetime2(3) default CURRENT_TIMESTAMP,
    Message nvarchar(max)
)

GO

CREATE PROCEDURE dbo.LogSchemaMigrationProgress
    @message varchar(max)
AS
    INSERT INTO dbo.SchemaMigrationProgress (Message) VALUES (@message)
GO

/*************************************************************
    Partitioning function and scheme
**************************************************************/

CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId (smallint)
AS RANGE RIGHT FOR VALUES (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150);

CREATE PARTITION SCHEME PartitionScheme_ResourceTypeId
AS PARTITION PartitionFunction_ResourceTypeId ALL TO ([PRIMARY]);

/*************************************************************
    Model tables
**************************************************************/

CREATE TABLE dbo.SearchParam
(
    SearchParamId smallint IDENTITY(1,1) NOT NULL,
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NULL,
    LastUpdated datetimeoffset(7) NULL,
    IsPartiallySupported bit NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_SearchParam ON dbo.SearchParam
(
    Uri
)

CREATE TABLE dbo.ResourceType
(
    ResourceTypeId smallint IDENTITY(1,1) NOT NULL,
    Name nvarchar(50) COLLATE Latin1_General_100_CS_AS  NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ResourceType on dbo.ResourceType
(
    Name
)

-- Create System and QuantityCode tables

CREATE TABLE dbo.System
(
    SystemId int IDENTITY(1,1) NOT NULL,
    Value nvarchar(256) NOT NULL,
)

CREATE UNIQUE CLUSTERED INDEX IXC_System ON dbo.System
(
    Value
)

CREATE TABLE dbo.QuantityCode
(
    QuantityCodeId int IDENTITY(1,1) NOT NULL,
    Value nvarchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_QuantityCode on dbo.QuantityCode
(
    Value
)

/*************************************************************
    Resource table
**************************************************************/

CREATE TABLE dbo.Resource
(
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Version int NOT NULL,
    IsHistory bit NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    IsDeleted bit NOT NULL,
    RequestMethod varchar(10) NULL,
    RawResource varbinary(max) NOT NULL,
    IsRawResourceMetaSet bit NOT NULL DEFAULT 0,
    SearchParamHash varchar(64) NULL
)

ALTER TABLE dbo.Resource SET ( LOCK_ESCALATION = AUTO )

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)


CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource
(
    ResourceTypeId,
    ResourceId
)
INCLUDE -- We want the query in UpsertResource, which is done with UPDLOCK AND HOLDLOCK, to not require a key lookup
(
    Version,
    IsDeleted
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsDeleted = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_Resource_ResourceSurrogateId ON dbo.Resource
(
    ResourceSurrogateId
)
ON [Primary]

/*************************************************************
    Capture claims on write
**************************************************************/

CREATE TABLE dbo.ClaimType
(
    ClaimTypeId tinyint IDENTITY(1,1) NOT NULL,
    Name varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Claim on dbo.ClaimType
(
    Name
)

CREATE TYPE dbo.BulkResourceWriteClaimTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ClaimTypeId tinyint NOT NULL,
    ClaimValue nvarchar(128) NOT NULL
)

CREATE TABLE dbo.ResourceWriteClaim
(
    ResourceSurrogateId bigint NOT NULL,
    ClaimTypeId tinyint NOT NULL,
    ClaimValue nvarchar(128) NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_ResourceWriteClaim on dbo.ResourceWriteClaim
(
    ResourceSurrogateId,
    ClaimTypeId
)

/*************************************************************
    Compartments
**************************************************************/

CREATE TABLE dbo.CompartmentType
(
    CompartmentTypeId tinyint IDENTITY(1,1) NOT NULL,
    Name varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_CompartmentType on dbo.CompartmentType
(
    Name
)

CREATE TYPE dbo.BulkCompartmentAssignmentTableType_1 AS TABLE
(
    Offset int NOT NULL,
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE TABLE dbo.CompartmentAssignment
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.CompartmentAssignment SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_CompartmentAssignment
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    ResourceSurrogateId,
    CompartmentTypeId,
    ReferenceResourceId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    CompartmentTypeId,
    ReferenceResourceId,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Reference Search Param
**************************************************************/

CREATE TYPE dbo.BulkReferenceSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL
)

CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL,
    IsHistory bit NOT NULL,
)

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

CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId,
    ReferenceResourceTypeId,
    BaseUri,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceVersion
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Token Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE TABLE dbo.TokenSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
)

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

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Token Text
**************************************************************/

CREATE TYPE dbo.BulkTokenTextTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL
)

CREATE TABLE dbo.TokenText
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.TokenText SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    String Search Param
**************************************************************/

CREATE TYPE dbo.BulkStringSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)

CREATE TABLE dbo.StringSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.StringSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_Text
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
INCLUDE
(
    TextOverflow -- will not be needed when all servers are targeting at least this version.
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND TextOverflow IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    URI Search Param
**************************************************************/

CREATE TYPE dbo.BulkUriSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE TABLE dbo.UriSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.UriSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_UriSearchParam
ON dbo.UriSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_UriSearchParam_SearchParamId_Uri
ON dbo.UriSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Uri,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO


/*************************************************************
    Number Search Param
**************************************************************/

-- We support the underlying value being a range, though we expect the vast majority of entries to be a single value.
-- Either:
--  (1) SingleValue is not null and LowValue and HighValue are both null, or
--  (2) SingleValue is null and LowValue and HighValue are both not null
-- We make use of filtered nonclustered indexes to keep queries over the ranges limited to those rows that actually have ranges

CREATE TYPE dbo.BulkNumberSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NULL,
    HighValue decimal(18,6) NULL
)

CREATE TABLE dbo.NumberSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) SPARSE NULL,
    HighValue decimal(18,6) SPARSE NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.NumberSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    SingleValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Quantity Search Param
**************************************************************/

-- See comment above for number search params for how we store ranges

CREATE TYPE dbo.BulkQuantitySearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) NULL,
    HighValue decimal(18,6) NULL
)

CREATE TABLE dbo.QuantitySearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) SPARSE NULL,
    HighValue decimal(18,6) SPARSE NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.QuantitySearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    SingleValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    LowValue,
    HighValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    SearchParamId,
    QuantityCodeId,
    HighValue,
    LowValue,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Date Search Param
**************************************************************/

CREATE TYPE dbo.BulkDateTimeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetimeoffset(7) NOT NULL,
    EndDateTime datetimeoffset(7) NOT NULL,
    IsLongerThanADay bit NOT NULL
)

CREATE TABLE dbo.DateTimeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.DateTimeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsLongerThanADay
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    ResourceSurrogateId
)
INCLUDE
(
    IsLongerThanADay
)
WHERE IsHistory = 0
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    StartDateTime,
    EndDateTime,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    EndDateTime,
    StartDateTime,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Reference$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkReferenceTokenCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE TABLE dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.ReferenceTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    ReferenceResourceId1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    ReferenceResourceTypeId1,
    BaseUri1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Token$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenTokenCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
)

CREATE TABLE dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL
)

ALTER TABLE dbo.TokenTokenCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Code2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Token$DateTime Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2 datetimeoffset(7) NOT NULL,
    EndDateTime2 datetimeoffset(7) NOT NULL,
    IsLongerThanADay2 bit NOT NULL
)

CREATE TABLE dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    StartDateTime2 datetime2(7) NOT NULL,
    EndDateTime2 datetime2(7) NOT NULL,
    IsLongerThanADay2 bit NOT NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.TokenDateTimeCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)

WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    IsLongerThanADay2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)

WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Token$Quantity Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    QuantityCodeId2 int NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL
)

CREATE TABLE dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SystemId2 int NULL,
    QuantityCodeId2 int NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.TokenQuantityCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    SingleValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND SingleValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    HighValue2,
    LowValue2,
    ResourceSurrogateId
)
INCLUDE
(
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Token$String Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenStringCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)

CREATE TABLE dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_CI_AI NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_CI_AI NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.TokenStringCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
ON dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1,
    TextOverflow2 -- will not be needed when all servers are targeting at least this version.
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow
ON dbo.TokenStringCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    Text2,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND TextOverflow2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO


/*************************************************************
    Token$Number$Number Composite Search Param
**************************************************************/

-- See number search param for how we deal with null. We apply a similar pattern here,
-- except that we pass in a HasRange bit though the TVP. The alternative would have
-- for a computed column, but a computed column cannot be used in as a index filter
-- (even if it is a persisted computed column).

CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    SingleValue3 decimal(18,6) NULL,
    LowValue3 decimal(18,6) NULL,
    HighValue3 decimal(18,6) NULL,
    HasRange bit NOT NULL
)

CREATE TABLE dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    SingleValue3 decimal(18,6) NULL,
    LowValue3 decimal(18,6) NULL,
    HighValue3 decimal(18,6) NULL,
    HasRange bit NOT NULL,
    IsHistory bit NOT NULL,
)

ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam SET ( LOCK_ESCALATION = AUTO )

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    SingleValue2,
    SingleValue3,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 0
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    LowValue3,
    HighValue3,
    ResourceSurrogateId
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 1
WITH (DATA_COMPRESSION = PAGE)
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO

/*************************************************************
    Sequence for generating unique 12.5ns "tick" components that are added
    to a base ID based on the timestamp to form a unique resource surrogate ID
**************************************************************/

CREATE SEQUENCE dbo.ResourceSurrogateIdUniquifierSequence
        AS int
        START WITH 0
        INCREMENT BY 1
        MINVALUE 0
        MAXVALUE 79999
        CYCLE
        CACHE 1000000
GO

/*************************************************************
    Stored procedures for creating and deleting
**************************************************************/

--
-- STORED PROCEDURE
--     UpsertResource_3
--
-- DESCRIPTION
--     Creates or updates (including marking deleted) a FHIR resource
--
-- PARAMETERS
--     @baseResourceSurrogateId
--         * A bigint to which a value between [0, 80000) is added, forming a unique ResourceSurrogateId.
--         * This value should be the current UTC datetime, truncated to millisecond precision, with its 100ns ticks component bitshifted left by 3.
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as the in the resource itself)
--     @etag
--         * If specified, the version of the resource to update
--     @allowCreate
--         * If false, an error is thrown if the resource does not already exist
--     @isDeleted
--         * Whether this resource marks the resource as deleted
--     @keepHistory
--         * Whether the existing version of the resource should be preserved
--     @requestMethod
--         * The HTTP method/verb used for the request
--     @searchParamHash
--          * A hash of the resource's latest indexed search parameters
--     @rawResource
--         * A compressed UTF16-encoded JSON document
--     @resourceWriteClaims
--         * Claims on the principal that performed the write
--     @compartmentAssignments
--         * Compartments that the resource is part of
--     @referenceSearchParams
--         * Extracted reference search params
--     @tokenSearchParams
--         * Extracted token search params
--     @tokenTextSearchParams
--         * The text representation of extracted token search params
--     @stringSearchParams
--         * Extracted string search params
--     @numberSearchParams
--         * Extracted number search params
--     @quantitySearchParams
--         * Extracted quantity search params
--     @uriSearchParams
--         * Extracted URI search params
--     @dateTimeSearchParms
--         * Extracted datetime search params
--     @referenceTokenCompositeSearchParams
--         * Extracted reference$token search params
--     @tokenTokenCompositeSearchParams
--         * Extracted token$token tokensearch params
--     @tokenDateTimeCompositeSearchParams
--         * Extracted token$datetime search params
--     @tokenQuantityCompositeSearchParams
--         * Extracted token$quantity search params
--     @tokenStringCompositeSearchParams
--         * Extracted token$string search params
--     @tokenNumberNumberCompositeSearchParams
--         * Extracted token$number$number search params
--
-- RETURN VALUE
--         The version of the resource as a result set. Will be empty if no insertion was done.
--
CREATE PROCEDURE dbo.UpsertResource_3
    @baseResourceSurrogateId bigint,
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @keepHistory bit,
    @requestMethod varchar(10),
    @searchParamHash varchar(64),
    @rawResource varbinary(max),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    -- variables for the existing version of the resource that will be replaced
    DECLARE @previousResourceSurrogateId bigint
    DECLARE @previousVersion bigint
    DECLARE @previousIsDeleted bit

    -- This should place a range lock on a row in the IX_Resource_ResourceTypeId_ResourceId nonclustered filtered index
    SELECT @previousResourceSurrogateId = ResourceSurrogateId, @previousVersion = Version, @previousIsDeleted = IsDeleted
    FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0

    IF (@etag IS NOT NULL AND @etag <> @previousVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    DECLARE @version int -- the version of the resource being written

    IF (@previousResourceSurrogateId IS NULL) BEGIN
        -- There is no previous version of this resource

        IF (@isDeleted = 1) BEGIN
            -- Don't bother marking the resource as deleted since it already does not exist.
            COMMIT TRANSACTION
            RETURN
        END

        IF (@etag IS NOT NULL) BEGIN
        -- You can't update a resource with a specified version if the resource does not exist
            THROW 50404, 'Resource with specified version not found', 1;
        END

        IF (@allowCreate = 0) BEGIN
            THROW 50405, 'Resource does not exist and create is not allowed', 1;
        END

        SET @version = 1
    END
    ELSE BEGIN
        -- There is a previous version

        IF (@isDeleted = 1 AND @previousIsDeleted = 1) BEGIN
            -- Already deleted - don't create a new version
            COMMIT TRANSACTION
            RETURN
        END

        SET @version = @previousVersion + 1

        IF (@keepHistory = 1) BEGIN

            -- Set the existing resource as history
            UPDATE dbo.Resource
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            -- Set the indexes for this resource as history.
            -- Note there is no IsHistory column on ResourceWriteClaim since we do not query it.

            UPDATE dbo.CompartmentAssignment
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenText
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.StringSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.UriSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.NumberSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.QuantitySearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.DateTimeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenDateTimeCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenQuantityCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenStringCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenNumberNumberCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

        END
        ELSE BEGIN

            -- Not keeping history. Delete the current resource and all associated indexes.

            DELETE FROM dbo.Resource
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ResourceWriteClaim
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.CompartmentAssignment
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenText
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.StringSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.UriSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.NumberSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.QuantitySearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.DateTimeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceTokenCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenTokenCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenDateTimeCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenQuantityCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenStringCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
            WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @previousResourceSurrogateId

        END
    END

    DECLARE @resourceSurrogateId bigint = @baseResourceSurrogateId + (NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence)
    DECLARE @isRawResourceMetaSet bit

    IF (@version = 1) BEGIN SET @isRawResourceMetaSet = 1 END ELSE BEGIN SET @isRawResourceMetaSet = 0 END

    INSERT INTO dbo.Resource
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
    VALUES
        (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, @isDeleted, @requestMethod, @rawResource, @isRawResourceMetaSet, @searchParamHash)

    INSERT INTO dbo.ResourceWriteClaim
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT @resourceSurrogateId, ClaimTypeId, ClaimValue
    FROM @resourceWriteClaims

    INSERT INTO dbo.CompartmentAssignment
        (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId, 0
    FROM @compartmentAssignments

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, 0
    FROM @referenceSearchParams

    INSERT INTO dbo.TokenSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, Code, 0
    FROM @tokenSearchParams

    INSERT INTO dbo.TokenText
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, 0
    FROM @tokenTextSearchParams

    INSERT INTO dbo.StringSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0
    FROM @stringSearchParams

    INSERT INTO dbo.UriSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Uri, 0
    FROM @uriSearchParams

    INSERT INTO dbo.NumberSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, 0
    FROM @numberSearchParams

    INSERT INTO dbo.QuantitySearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, 0
    FROM @quantitySearchParams

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, 0
    FROM @dateTimeSearchParms

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, 0
    FROM @referenceTokenCompositeSearchParams

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, 0
    FROM @tokenTokenCompositeSearchParams

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, 0
    FROM @tokenDateTimeCompositeSearchParams

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, 0
    FROM @tokenNumberNumberCompositeSearchParams

    SELECT @version

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     ReadResource
--
-- DESCRIPTION
--     Reads a single resource, optionally a specific version of the resource.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID
--     @version
--         * A specific version of the resource. If null, returns the latest version.
-- RETURN VALUE
--         A result set with 0 or 1 rows.
--
CREATE PROCEDURE dbo.ReadResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @version int = NULL
AS
    SET NOCOUNT ON

    IF (@version IS NULL) BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0
    END
    ELSE BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource, IsRawResourceMetaSet, SearchParamHash
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND Version = @version
    END
GO

--
-- STORED PROCEDURE
--     Deletes a single resource
--
-- DESCRIPTION
--     Permanently deletes all data related to a resource.
--     Data remains recoverable from the transaction log, however.
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as in the resource itself)
--
CREATE PROCEDURE dbo.HardDeleteResource
    @resourceTypeId smallint,
    @resourceId varchar(64)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @resourceSurrogateIds TABLE(ResourceSurrogateId bigint NOT NULL)

    DELETE FROM dbo.Resource
    OUTPUT deleted.ResourceSurrogateId
    INTO @resourceSurrogateIds
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId

    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenText
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    COMMIT TRANSACTION
GO

/*************************************************************
    Export Job
**************************************************************/
CREATE TABLE dbo.ExportJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Hash varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ExportJob ON dbo.ExportJob
(
    Id
)

CREATE UNIQUE NONCLUSTERED INDEX IX_ExportJob_Hash_Status_HeartbeatDateTime ON dbo.ExportJob
(
    Hash,
    Status,
    HeartbeatDateTime
)

GO

/*************************************************************
    Stored procedures for exporting
**************************************************************/
--
-- STORED PROCEDURE
--     Creates an export job.
--
-- DESCRIPTION
--     Creates a new row to the ExportJob table, adding a new job to the queue of jobs to be processed.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record
--     @hash
--         * The SHA256 hash of the export job record ID
--     @status
--         * The status of the export job
--     @rawJobRecord
--         * A JSON document
--
-- RETURN VALUE
--     The row version of the created export job.
--
CREATE PROCEDURE dbo.CreateExportJob
    @id varchar(64),
    @hash varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    INSERT INTO dbo.ExportJob
        (Id, Hash, Status, HeartbeatDateTime, RawJobRecord)
    VALUES
        (@id, @hash, @status, @heartbeatDateTime, @rawJobRecord)

    SELECT CAST(MIN_ACTIVE_ROWVERSION() AS INT)

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Gets an export job given its ID.
--
-- DESCRIPTION
--     Retrieves the export job record from the ExportJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record to retrieve
--
-- RETURN VALUE
--     The matching export job.
--
CREATE PROCEDURE dbo.GetExportJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ExportJob
    WHERE Id = @id
GO

--
-- STORED PROCEDURE
--     Gets an export job given the hash of its ID.
--
-- DESCRIPTION
--     Retrieves the export job record from the ExportJob table that has the matching hash.
--
-- PARAMETERS
--     @hash
--         * The SHA256 hash of the export job record ID
--
-- RETURN VALUE
--     The matching export job.
--
CREATE PROCEDURE dbo.GetExportJobByHash
    @hash varchar(64)
AS
    SET NOCOUNT ON

    SELECT TOP(1) RawJobRecord, JobVersion
    FROM dbo.ExportJob
    WHERE Hash = @hash AND (Status = 'Queued' OR Status = 'Running')
    ORDER BY HeartbeatDateTime ASC
GO

--
-- STORED PROCEDURE
--     Updates an export job.
--
-- DESCRIPTION
--     Modifies an existing job in the ExportJob table.
--
-- PARAMETERS
--     @id
--         * The ID of the export job record
--     @status
--         * The status of the export job
--     @rawJobRecord
--         * A JSON document
--     @jobVersion
--         * The version of the job to update must match this
--
-- RETURN VALUE
--     The row version of the updated export job.
--
CREATE PROCEDURE dbo.UpdateExportJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max),
    @jobVersion binary(8)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @currentJobVersion binary(8)

    -- Acquire and hold an update lock on a row in the ExportJob table for the entire transaction.
    -- This ensures the version check and update occur atomically.
    SELECT @currentJobVersion = JobVersion
    FROM dbo.ExportJob WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = @id

    IF (@currentJobVersion IS NULL) BEGIN
        THROW 50404, 'Export job record not found', 1;
    END

    IF (@jobVersion <> @currentJobVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.ExportJob
    SET Status = @status, HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = @rawJobRecord
    WHERE Id = @id

    SELECT @@DBTS

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Acquires export jobs.
--
-- DESCRIPTION
--     Timestamps the available export jobs and sets their statuses to running.
--
-- PARAMETERS
--     @jobHeartbeatTimeoutThresholdInSeconds
--         * The number of seconds that must pass before an export job is considered stale
--     @maximumNumberOfConcurrentJobsAllowed
--         * The maximum number of running jobs we can have at once
--
-- RETURN VALUE
--     The updated jobs that are now running.
--
CREATE PROCEDURE dbo.AcquireExportJobs
    @jobHeartbeatTimeoutThresholdInSeconds bigint,
    @maximumNumberOfConcurrentJobsAllowed int
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    -- We will consider a job to be stale if its timestamp is smaller than or equal to this.
    DECLARE @expirationDateTime dateTime2(7)
    SELECT @expirationDateTime = DATEADD(second, -@jobHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME())

    -- Get the number of jobs that are running and not stale.
    -- Acquire and hold an exclusive table lock for the entire transaction to prevent jobs from being created, updated or deleted during acquisitions.
    DECLARE @numberOfRunningJobs int
    SELECT @numberOfRunningJobs = COUNT(*) FROM dbo.ExportJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > @expirationDateTime

    -- Determine how many available jobs we can pick up.
    DECLARE @limit int = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;

    DECLARE @availableJobs TABLE (Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL, JobVersion binary(8) NOT NULL)

    -- Get the available jobs, which are export jobs that are queued or stale.
    -- Older jobs will be prioritized over newer ones.
    INSERT INTO @availableJobs
    SELECT TOP(@limit) Id, JobVersion
    FROM dbo.ExportJob
    WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= @expirationDateTime))
    ORDER BY HeartbeatDateTime

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    -- Update each available job's status to running both in the export table's status column and in the raw export job record JSON.
    UPDATE dbo.ExportJob
    SET Status = 'Running', HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = JSON_MODIFY(RawJobRecord,'$.status', 'Running')
    OUTPUT inserted.RawJobRecord, inserted.JobVersion
    FROM dbo.ExportJob job INNER JOIN @availableJobs availableJob ON job.Id = availableJob.Id AND job.JobVersion = availableJob.JobVersion

    COMMIT TRANSACTION
GO

/*************************************************************
    Search Parameter Status Information
**************************************************************/

-- We adopted this naming convention for table-valued parameters because they are immutable.
CREATE TYPE dbo.SearchParamTableType_1 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    IsPartiallySupported bit NOT NULL
)

GO

/*************************************************************
    Stored procedures for search parameter information
**************************************************************/
--
-- STORED PROCEDURE
--     GetSearchParamStatuses
--
-- DESCRIPTION
--     Gets all the search parameters and their statuses.
--
-- RETURN VALUE
--     The search parameters and their statuses.
--
CREATE PROCEDURE dbo.GetSearchParamStatuses
AS
    SET NOCOUNT ON

    SELECT Uri, Status, LastUpdated, IsPartiallySupported FROM dbo.SearchParam
GO

--
-- STORED PROCEDURE
--     UpsertSearchParams
--
-- DESCRIPTION
--     Given a set of search parameters, creates or updates the parameters.
--
-- PARAMETERS
--     @searchParams
--         * The updated existing search parameters or the new search parameters
--
-- RETURN VALUE
--     The IDs and URIs of the search parameters that were inserted (not updated).
--
CREATE PROCEDURE dbo.UpsertSearchParams
    @searchParams dbo.SearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()

    DECLARE @summaryOfChanges TABLE(Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL, Action varchar(20) NOT NULL)

    -- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsert.
    MERGE INTO dbo.SearchParam WITH (TABLOCKX) AS target
    USING @searchParams AS source
    ON target.Uri = source.Uri
    WHEN MATCHED THEN
        UPDATE
            SET Status = source.Status, LastUpdated = @lastUpdated, IsPartiallySupported = source.IsPartiallySupported
    WHEN NOT MATCHED BY target THEN
        INSERT
            (Uri, Status, LastUpdated, IsPartiallySupported)
            VALUES (source.Uri, source.Status, @lastUpdated, source.IsPartiallySupported)
    OUTPUT source.Uri, $action INTO @summaryOfChanges;

    SELECT SearchParamId, SearchParam.Uri
    FROM dbo.SearchParam searchParam
    INNER JOIN @summaryOfChanges upsertedSearchParam
    ON searchParam.Uri = upsertedSearchParam.Uri
    WHERE upsertedSearchParam.Action = 'INSERT'

    COMMIT TRANSACTION
GO

/*************************************************************
    Reindex Job
**************************************************************/
CREATE TABLE dbo.ReindexJob
(
    Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    HeartbeatDateTime datetime2(7) NULL,
    RawJobRecord varchar(max) NOT NULL,
    JobVersion rowversion NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_ReindexJob ON dbo.ReindexJob
(
    Id
)

GO

/*************************************************************
    Stored procedures for reindexing
**************************************************************/
--
-- STORED PROCEDURE
--     Creates an reindex job.
--
-- DESCRIPTION
--     Creates a new row to the ReindexJob table, adding a new job to the queue of jobs to be processed.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record
--     @status
--         * The status of the reindex job
--     @rawJobRecord
--         * A JSON document
--
-- RETURN VALUE
--     The row version of the created reindex job.
--
CREATE PROCEDURE dbo.CreateReindexJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    INSERT INTO dbo.ReindexJob
        (Id, Status, HeartbeatDateTime, RawJobRecord)
    VALUES
        (@id, @status, @heartbeatDateTime, @rawJobRecord)

    SELECT CAST(MIN_ACTIVE_ROWVERSION() AS INT)

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Gets an reindex job given its ID.
--
-- DESCRIPTION
--     Retrieves the reindex job record from the ReindexJob table that has the matching ID.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record to retrieve
--
-- RETURN VALUE
--     The matching reindex job.
--
CREATE PROCEDURE dbo.GetReindexJobById
    @id varchar(64)
AS
    SET NOCOUNT ON

    SELECT RawJobRecord, JobVersion
    FROM dbo.ReindexJob
    WHERE Id = @id
GO

--
-- STORED PROCEDURE
--     Updates a reindex job.
--
-- DESCRIPTION
--     Modifies an existing job in the ReindexJob table.
--
-- PARAMETERS
--     @id
--         * The ID of the reindex job record
--     @status
--         * The status of the reindex job
--     @rawJobRecord
--         * A JSON document
--     @jobVersion
--         * The version of the job to update must match this
--
-- RETURN VALUE
--     The row version of the updated reindex job.
--
CREATE PROCEDURE dbo.UpdateReindexJob
    @id varchar(64),
    @status varchar(10),
    @rawJobRecord varchar(max),
    @jobVersion binary(8)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @currentJobVersion binary(8)

    -- Acquire and hold an update lock on a row in the ReindexJob table for the entire transaction.
    -- This ensures the version check and update occur atomically.
    SELECT @currentJobVersion = JobVersion
    FROM dbo.ReindexJob WITH (UPDLOCK, HOLDLOCK)
    WHERE Id = @id

    IF (@currentJobVersion IS NULL) BEGIN
        THROW 50404, 'Reindex job record not found', 1;
    END

    IF (@jobVersion <> @currentJobVersion) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.ReindexJob
    SET Status = @status, HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = @rawJobRecord
    WHERE Id = @id

    SELECT @@DBTS

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Acquires reindex jobs.
--
-- DESCRIPTION
--     Timestamps the available reindex jobs and sets their statuses to running.
--
-- PARAMETERS
--     @jobHeartbeatTimeoutThresholdInSeconds
--         * The number of seconds that must pass before a reindex job is considered stale
--     @maximumNumberOfConcurrentJobsAllowed
--         * The maximum number of running jobs we can have at once
--
-- RETURN VALUE
--     The updated jobs that are now running.
--
CREATE PROCEDURE dbo.AcquireReindexJobs
    @jobHeartbeatTimeoutThresholdInSeconds bigint,
    @maximumNumberOfConcurrentJobsAllowed int
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    -- We will consider a job to be stale if its timestamp is smaller than or equal to this.
    DECLARE @expirationDateTime dateTime2(7)
    SELECT @expirationDateTime = DATEADD(second, -@jobHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME())

    -- Get the number of jobs that are running and not stale.
    -- Acquire and hold an exclusive table lock for the entire transaction to prevent jobs from being created, updated or deleted during acquisitions.
    DECLARE @numberOfRunningJobs int
    SELECT @numberOfRunningJobs = COUNT(*) FROM dbo.ReindexJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > @expirationDateTime

    -- Determine how many available jobs we can pick up.
    DECLARE @limit int = @maximumNumberOfConcurrentJobsAllowed - @numberOfRunningJobs;

    IF (@limit > 0) BEGIN

        DECLARE @availableJobs TABLE (Id varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL, JobVersion binary(8) NOT NULL)

        -- Get the available jobs, which are reindex jobs that are queued or stale.
        -- Older jobs will be prioritized over newer ones.
        INSERT INTO @availableJobs
        SELECT TOP(@limit) Id, JobVersion
        FROM dbo.ReindexJob
        WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= @expirationDateTime))
        ORDER BY HeartbeatDateTime

        DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

        -- Update each available job's status to running both in the reindex table's status column and in the raw reindex job record JSON.
        UPDATE dbo.ReindexJob
        SET Status = 'Running', HeartbeatDateTime = @heartbeatDateTime, RawJobRecord = JSON_MODIFY(RawJobRecord,'$.status', 'Running')
        OUTPUT inserted.RawJobRecord, inserted.JobVersion
        FROM dbo.ReindexJob job INNER JOIN @availableJobs availableJob ON job.Id = availableJob.Id AND job.JobVersion = availableJob.JobVersion

    END

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Checks if there are any active reindex jobs.
--
-- DESCRIPTION
--     Queries the datastore for any reindex job documents with a status of running, queued or paused.
--
-- RETURN VALUE
--     The job IDs of any active reindex jobs.
--
CREATE PROCEDURE dbo.CheckActiveReindexJobs
AS
    SET NOCOUNT ON

    SELECT Id
    FROM dbo.ReindexJob
    WHERE Status = 'Running' OR Status = 'Queued' OR Status = 'Paused'
GO

--
-- STORED PROCEDURE
--     ReindexResource
--
-- DESCRIPTION
--     Updates the search indices of a given resource
--
-- PARAMETERS
--     @resourceTypeId
--         * The ID of the resource type (See ResourceType table)
--     @resourceId
--         * The resource ID (must be the same as the in the resource itself)
--     @etag
--         * If specified, the version of the resource to update
--     @searchParamHash
--          * A hash of the resource's latest indexed search parameters
--     @resourceWriteClaims
--         * Claims on the principal that performed the write
--     @compartmentAssignments
--         * Compartments that the resource is part of
--     @referenceSearchParams
--         * Extracted reference search params
--     @tokenSearchParams
--         * Extracted token search params
--     @tokenTextSearchParams
--         * The text representation of extracted token search params
--     @stringSearchParams
--         * Extracted string search params
--     @numberSearchParams
--         * Extracted number search params
--     @quantitySearchParams
--         * Extracted quantity search params
--     @uriSearchParams
--         * Extracted URI search params
--     @dateTimeSearchParms
--         * Extracted datetime search params
--     @referenceTokenCompositeSearchParams
--         * Extracted reference$token search params
--     @tokenTokenCompositeSearchParams
--         * Extracted token$token tokensearch params
--     @tokenDateTimeCompositeSearchParams
--         * Extracted token$datetime search params
--     @tokenQuantityCompositeSearchParams
--         * Extracted token$quantity search params
--     @tokenStringCompositeSearchParams
--         * Extracted token$string search params
--     @tokenNumberNumberCompositeSearchParams
--         * Extracted token$number$number search params
--
CREATE PROCEDURE dbo.ReindexResource
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @searchParamHash varchar(64),
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @resourceSurrogateId bigint
    DECLARE @version bigint

    -- This should place a range lock on a row in the IX_Resource_ResourceTypeId_ResourceId nonclustered filtered index
    SELECT @resourceSurrogateId = ResourceSurrogateId, @version = Version
    FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
    WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0

    IF (@etag IS NOT NULL AND @etag <> @version) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    IF (@resourceSurrogateId IS NULL) BEGIN
        -- You can't reindex a resource if the resource does not exist
        THROW 50404, 'Resource not found', 1;
    END

    UPDATE dbo.Resource
    SET SearchParamHash = @searchParamHash
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    -- First, delete all the resource's indices.
    DELETE FROM dbo.ResourceWriteClaim
    WHERE ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.CompartmentAssignment
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenText
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceTypeId = @resourceTypeId AND ResourceSurrogateId = @resourceSurrogateId

    -- Next, insert all the new indices.
    INSERT INTO dbo.ResourceWriteClaim
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT @resourceSurrogateId, ClaimTypeId, ClaimValue
    FROM @resourceWriteClaims

    INSERT INTO dbo.CompartmentAssignment
        (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId, 0
    FROM @compartmentAssignments

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, 0
    FROM @referenceSearchParams

    INSERT INTO dbo.TokenSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, Code, 0
    FROM @tokenSearchParams

    INSERT INTO dbo.TokenText
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, 0
    FROM @tokenTextSearchParams

    INSERT INTO dbo.StringSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0
    FROM @stringSearchParams

    INSERT INTO dbo.UriSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, Uri, 0
    FROM @uriSearchParams

    INSERT INTO dbo.NumberSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, 0
    FROM @numberSearchParams

    INSERT INTO dbo.QuantitySearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, 0
    FROM @quantitySearchParams

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, 0
    FROM @dateTimeSearchParms

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, 0
    FROM @referenceTokenCompositeSearchParams

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, 0
    FROM @tokenTokenCompositeSearchParams

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, 0
    FROM @tokenDateTimeCompositeSearchParams

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT @resourceTypeId, @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, 0
    FROM @tokenNumberNumberCompositeSearchParams

    COMMIT TRANSACTION
GO

CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag int NULL,
    SearchParamHash varchar(64) NOT NULL
)

GO

--
-- STORED PROCEDURE
--     BulkReindexResources
--
-- DESCRIPTION
--     Updates the search indices of a batch of resources
--
-- PARAMETERS
--     @resourcesToReindex
--         * The type IDs, IDs, eTags and hashes of the resources to reindex
--     @resourceWriteClaims
--         * Claims on the principal that performed the write
--     @compartmentAssignments
--         * Compartments that the resource is part of
--     @referenceSearchParams
--         * Extracted reference search params
--     @tokenSearchParams
--         * Extracted token search params
--     @tokenTextSearchParams
--         * The text representation of extracted token search params
--     @stringSearchParams
--         * Extracted string search params
--     @numberSearchParams
--         * Extracted number search params
--     @quantitySearchParams
--         * Extracted quantity search params
--     @uriSearchParams
--         * Extracted URI search params
--     @dateTimeSearchParms
--         * Extracted datetime search params
--     @referenceTokenCompositeSearchParams
--         * Extracted reference$token search params
--     @tokenTokenCompositeSearchParams
--         * Extracted token$token tokensearch params
--     @tokenDateTimeCompositeSearchParams
--         * Extracted token$datetime search params
--     @tokenQuantityCompositeSearchParams
--        * Extracted token$quantity search params
--     @tokenStringCompositeSearchParams
--         * Extracted token$string search params
--     @tokenNumberNumberCompositeSearchParams
--         * Extracted token$number$number search params
--
CREATE PROCEDURE dbo.BulkReindexResources
    @resourcesToReindex dbo.BulkReindexResourceTableType_1 READONLY,
    @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY,
    @stringSearchParams dbo.BulkStringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @computedValues TABLE
    (
        Offset int NOT NULL,
        ResourceTypeId smallint NOT NULL,
        VersionProvided bigint NULL,
        SearchParamHash varchar(64) NOT NULL,
        ResourceSurrogateId bigint NULL,
        VersionInDatabase bigint NULL
    )

    INSERT INTO @computedValues
    SELECT
        resourceToReindex.Offset,
        resourceToReindex.ResourceTypeId,
        resourceToReindex.ETag,
        resourceToReindex.SearchParamHash,
        resourceInDB.ResourceSurrogateId,
        resourceInDB.Version
    FROM @resourcesToReindex resourceToReindex
    LEFT OUTER JOIN dbo.Resource resourceInDB WITH (UPDLOCK, INDEX(IX_Resource_ResourceTypeId_ResourceId))
        ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
            AND resourceInDB.ResourceId = resourceToReindex.ResourceId
            AND resourceInDB.IsHistory = 0

    DECLARE @resourcesNotInDatabase int
    SET @resourcesNotInDatabase = (SELECT COUNT(*) FROM @computedValues WHERE ResourceSurrogateId IS NULL)

    IF (@resourcesNotInDatabase > 0) BEGIN
        -- We can't reindex a resource if the resource does not exist
        THROW 50404, 'One or more resources not found', 1;
    END

    DECLARE @versionDiff int
    SET @versionDiff = (SELECT COUNT(*) FROM @computedValues WHERE VersionProvided IS NOT NULL AND VersionProvided <> VersionInDatabase)

    IF (@versionDiff > 0) BEGIN
        -- The resource has been updated since the reindex job kicked off
        THROW 50412, 'Precondition failed', 1;
    END

    -- Update the search parameter hash value in the main resource table
    UPDATE resourceInDB
    SET resourceInDB.SearchParamHash = resourceToReindex.SearchParamHash
    FROM @computedValues resourceToReindex
    INNER JOIN dbo.Resource resourceInDB
        ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId AND resourceInDB.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    -- First, delete all the indices of the resources to reindex.
    DELETE searchIndex FROM dbo.ResourceWriteClaim searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.CompartmentAssignment searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.ReferenceSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenText searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.StringSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.UriSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.NumberSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.QuantitySearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.DateTimeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.ReferenceTokenCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenTokenCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenDateTimeCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenQuantityCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenStringCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    DELETE searchIndex FROM dbo.TokenNumberNumberCompositeSearchParam searchIndex
    INNER JOIN @computedValues resourceToReindex
        ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId

    -- Next, insert all the new indices.
    INSERT INTO dbo.ResourceWriteClaim
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT DISTINCT resourceToReindex.ResourceSurrogateId, searchIndex.ClaimTypeId, searchIndex.ClaimValue
    FROM @resourceWriteClaims searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.CompartmentAssignment
        (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.CompartmentTypeId, searchIndex.ReferenceResourceId, 0
    FROM @compartmentAssignments searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.BaseUri, searchIndex.ReferenceResourceTypeId, searchIndex.ReferenceResourceId, searchIndex.ReferenceResourceVersion, 0
    FROM @referenceSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId, searchIndex.Code, 0
    FROM @tokenSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenText
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Text, 0
    FROM @tokenTextSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.StringSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Text, searchIndex.TextOverflow, 0
    FROM @stringSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.UriSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.Uri, 0
    FROM @uriSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.NumberSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SingleValue, searchIndex.LowValue, searchIndex.HighValue, 0
    FROM @numberSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.QuantitySearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId, searchIndex.QuantityCodeId, searchIndex.SingleValue, searchIndex.LowValue, searchIndex.HighValue, 0
    FROM @quantitySearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.StartDateTime, searchIndex.EndDateTime, searchIndex.IsLongerThanADay, 0
    FROM @dateTimeSearchParms searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.BaseUri1, searchIndex.ReferenceResourceTypeId1, searchIndex.ReferenceResourceId1, searchIndex.ReferenceResourceVersion1, searchIndex.SystemId2, searchIndex.Code2, 0
    FROM @referenceTokenCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.SystemId2, searchIndex.Code2, 0
    FROM @tokenTokenCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.StartDateTime2, searchIndex.EndDateTime2, searchIndex.IsLongerThanADay2, 0
    FROM @tokenDateTimeCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.SingleValue2, searchIndex.SystemId2, searchIndex.QuantityCodeId2, searchIndex.LowValue2, searchIndex.HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.Text2, searchIndex.TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT resourceToReindex.ResourceTypeId, resourceToReindex.ResourceSurrogateId, searchIndex.SearchParamId, searchIndex.SystemId1, searchIndex.Code1, searchIndex.SingleValue2, searchIndex.LowValue2, searchIndex.HighValue2, searchIndex.SingleValue3, searchIndex.LowValue3, searchIndex.HighValue3, searchIndex.HasRange, 0
    FROM @tokenNumberNumberCompositeSearchParams searchIndex
    INNER JOIN @computedValues resourceToReindex ON searchIndex.Offset = resourceToReindex.Offset

    COMMIT TRANSACTION
GO

/*************************************************************
    Task Table
**************************************************************/
CREATE TABLE [dbo].[TaskInfo](
	[TaskId] [varchar](64) NOT NULL,
	[QueueId] [varchar](64) NOT NULL,
	[Status] [smallint] NOT NULL,
    [TaskTypeId] [smallint] NOT NULL,
    [RunId] [varchar](50) null,
	[IsCanceled] [bit] NOT NULL,
    [RetryCount] [smallint] NOT NULL,
    [MaxRetryCount] [smallint] NOT NULL,
	[HeartbeatDateTime] [datetime2](7) NULL,
	[InputData] [varchar](max) NOT NULL,
	[TaskContext] [varchar](max) NULL,
    [Result] [varchar](max) NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

CREATE UNIQUE CLUSTERED INDEX IXC_Task on [dbo].[TaskInfo]
(
    TaskId
)
GO

/*************************************************************
    Stored procedures for general task
**************************************************************/
--
-- STORED PROCEDURE
--     CreateTask
--
-- DESCRIPTION
--     Create task for given task payload.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record to create
--     @queueId
--         * The number of seconds that must pass before an export job is considered stale
--     @taskTypeId
--         * The maximum number of running jobs we can have at once
--
CREATE PROCEDURE [dbo].[CreateTask]
    @taskId varchar(64),
    @queueId varchar(64),
	@taskTypeId smallint,
    @maxRetryCount smallint = 3,
    @inputData varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()
	DECLARE @status smallint = 1
    DECLARE @retryCount smallint = 0
	DECLARE @isCanceled bit = 0

    -- Check if the task already be created
    IF EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId
    ) BEGIN
        THROW 50409, 'Task already existed', 1;
    END

    -- Create new task
    INSERT INTO [dbo].[TaskInfo]
        (TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData)
    VALUES
        (@taskId, @queueId, @status, @taskTypeId, @isCanceled, @retryCount, @maxRetryCount, @heartbeatDateTime, @inputData)

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for get task payload
**************************************************************/
--
-- STORED PROCEDURE
--     GetTaskDetails
--
-- DESCRIPTION
--     Get task payload.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--
CREATE PROCEDURE [dbo].[GetTaskDetails]
    @taskId varchar(64)
AS
    SET NOCOUNT ON

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId
GO


/*************************************************************
    Stored procedures for update task context
**************************************************************/
--
-- STORED PROCEDURE
--     UpdateTaskContext
--
-- DESCRIPTION
--     Update task context.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @taskContext
--         * The context of the task
--     @runId
--         * Current runId for this exuction of the task
--
CREATE PROCEDURE [dbo].[UpdateTaskContext]
    @taskId varchar(64),
    @taskContext varchar(max),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only update task context with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET HeartbeatDateTime = @heartbeatDateTime, TaskContext = @taskContext
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO


/*************************************************************
    Stored procedures for keepalive task
**************************************************************/
--
-- STORED PROCEDURE
--     TaskKeepAlive
--
-- DESCRIPTION
--     Task keep-alive.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @runId
--         * Current runId for this exuction of the task
--
CREATE PROCEDURE [dbo].[TaskKeepAlive]
    @taskId varchar(64),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only update task context with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET HeartbeatDateTime = @heartbeatDateTime
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for complete task with result
**************************************************************/
--
-- STORED PROCEDURE
--     CompleteTask
--
-- DESCRIPTION
--     Complete the task and update task result.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @taskResult
--         * The result for the task execution
--     @runId
--         * Current runId for this exuction of the task
--
CREATE PROCEDURE [dbo].[CompleteTask]
    @taskId varchar(64),
    @taskResult varchar(max),
    @runId varchar(50)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only complete task with same runid
    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId and RunId = @runId
    ) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

	UPDATE dbo.TaskInfo
	SET Status = 3, HeartbeatDateTime = @heartbeatDateTime, Result = @taskResult
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO


/*************************************************************
    Stored procedures for cancel task
**************************************************************/
--
-- STORED PROCEDURE
--     CancelTask
--
-- DESCRIPTION
--     Cancel the task and update task status.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--
CREATE PROCEDURE [dbo].[CancelTask]
    @taskId varchar(64)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- We will timestamp the jobs when we update them to track stale jobs.
    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    IF NOT EXISTS
    (
        SELECT *
        FROM [dbo].[TaskInfo]
        WHERE TaskId = @taskId
    ) BEGIN
        THROW 50404, 'Task not exist', 1;
    END

	UPDATE dbo.TaskInfo
	SET IsCanceled = 1, HeartbeatDateTime = @heartbeatDateTime
	WHERE TaskId = @taskId

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO


/*************************************************************
    Stored procedures for reset task
**************************************************************/
--
-- STORED PROCEDURE
--     ResetTask
--
-- DESCRIPTION
--     Reset the task status.
--
-- PARAMETERS
--     @taskId
--         * The ID of the task record
--     @runId
--         * Current runId for this exuction of the task
--
CREATE PROCEDURE [dbo].[ResetTask]
    @taskId varchar(64),
    @runId varchar(50),
    @result varchar(max)
AS
    SET NOCOUNT ON

    SET XACT_ABORT ON
    BEGIN TRANSACTION
	
    -- Can only reset task with same runid
    DECLARE @retryCount smallint
    DECLARE @status smallint
    DECLARE @maxRetryCount smallint

    SELECT @retryCount = RetryCount, @status = Status, @maxRetryCount = MaxRetryCount
    FROM [dbo].[TaskInfo]
    WHERE TaskId = @taskId and RunId = @runId

	-- We will timestamp the jobs when we update them to track stale jobs.
    IF (@retryCount IS NULL) BEGIN
        THROW 50404, 'Task not exist or runid not match', 1;
    END

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    IF (@retryCount >= @maxRetryCount) BEGIN
		UPDATE dbo.TaskInfo
		SET Status = 3, HeartbeatDateTime = @heartbeatDateTime, Result = @result
		WHERE TaskId = @taskId
	END
    Else IF (@status <> 3) BEGIN
        UPDATE dbo.TaskInfo
		SET Status = 1, HeartbeatDateTime = @heartbeatDateTime, Result = @result, RetryCount = @retryCount + 1
		WHERE TaskId = @taskId
	END

    SELECT TaskId, QueueId, Status, TaskTypeId, RunId, IsCanceled, RetryCount, MaxRetryCount, HeartbeatDateTime, InputData, TaskContext, Result
	FROM [dbo].[TaskInfo]
	where TaskId = @taskId

    COMMIT TRANSACTION
GO

/*************************************************************
    Stored procedures for get next available task
**************************************************************/
--
-- STORED PROCEDURE
--     GetNextTask
--
-- DESCRIPTION
--     Get next available tasks
--
-- PARAMETERS
--     @queueId
--         * The ID of the task record
--     @count
--         * Batch count for tasks list
--     @taskHeartbeatTimeoutThresholdInSeconds
--         * Timeout threshold in seconds for heart keep alive
CREATE PROCEDURE [dbo].[GetNextTask]
    @queueId varchar(64),
	@count smallint,
    @taskHeartbeatTimeoutThresholdInSeconds int = 600
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    -- We will consider a job to be stale if its timestamp is smaller than or equal to this.
    DECLARE @expirationDateTime dateTime2(7)
    SELECT @expirationDateTime = DATEADD(second, -@taskHeartbeatTimeoutThresholdInSeconds, SYSUTCDATETIME())

    DECLARE @availableJobs TABLE (
		TaskId varchar(64),
		QueueId varchar(64),
		Status smallint,
        TaskTypeId smallint,
		IsCanceled bit,
        RetryCount smallint,
		HeartbeatDateTime datetime2,
		InputData varchar(max),
		TaskContext varchar(max),
        Result varchar(max)
	)

    INSERT INTO @availableJobs
    SELECT TOP(@count) TaskId, QueueId, Status, TaskTypeId, IsCanceled, RetryCount, HeartbeatDateTime, InputData, TaskContext, Result
    FROM dbo.TaskInfo
    WHERE (QueueId = @queueId AND ((Status = 1 OR (Status = 2 AND HeartbeatDateTime <= @expirationDateTime)) AND IsCanceled = 0))
    ORDER BY HeartbeatDateTime

    DECLARE @heartbeatDateTime datetime2(7) = SYSUTCDATETIME()

    UPDATE dbo.TaskInfo
    SET Status = 2, HeartbeatDateTime = @heartbeatDateTime, RunId = CAST(NEWID() AS NVARCHAR(50))
    FROM dbo.TaskInfo task INNER JOIN @availableJobs availableJob ON task.TaskId = availableJob.TaskId

	Select task.TaskId, task.QueueId, task.Status, task.TaskTypeId, task.RunId, task.IsCanceled, task.RetryCount, task.MaxRetryCount, task.HeartbeatDateTime, task.InputData, task.TaskContext, task.Result
	from dbo.TaskInfo task INNER JOIN @availableJobs availableJob ON task.TaskId = availableJob.TaskId

    COMMIT TRANSACTION
GO

