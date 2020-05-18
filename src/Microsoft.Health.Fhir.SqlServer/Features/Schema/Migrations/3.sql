-- NOTE: This script DROPS AND RECREATES all database objects.
-- Style guide: please see: https://github.com/ktaranov/sqlserver-kit/blob/master/SQL%20Server%20Name%20Convention%20and%20T-SQL%20Programming%20Style.md


/*************************************************************
    Drop existing objects
**************************************************************/

DECLARE @sql nvarchar(max) =''

SELECT @sql = @sql + 'DROP PROCEDURE ' + name + '; '
FROM sys.procedures

SELECT @sql = @sql + 'DROP TABLE ' + name + '; '
FROM sys.tables

SELECT @sql = @sql + 'DROP TYPE ' + name + '; '
FROM sys.table_types

SELECT @sql = @sql + 'DROP SEQUENCE ' + name + '; '
FROM sys.sequences

EXEC(@sql)

GO

/*************************************************************
    Configure database
**************************************************************/

-- Enable RCSI
IF ((SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()) = 0) BEGIN
    ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON
END

-- Avoid blocking queries when statistics need to be rebuilt
IF ((SELECT is_auto_update_stats_async_on FROM sys.databases WHERE database_id = DB_ID()) = 0) BEGIN
    ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS_ASYNC ON
END

-- Use ANSI behavior for null values
IF ((SELECT is_ansi_nulls_on FROM sys.databases WHERE database_id = DB_ID()) = 0) BEGIN
    ALTER DATABASE CURRENT SET ANSI_NULLS ON
END

GO

/*************************************************************
    Schema bootstrap
**************************************************************/

CREATE TABLE dbo.SchemaVersion
(
    Version int PRIMARY KEY,
    Status varchar(10)
)

INSERT INTO dbo.SchemaVersion
VALUES
    (3, 'started')

GO

--
--  STORED PROCEDURE
--      SelectCurrentSchemaVersion
--
--  DESCRIPTION
--      Selects the current completed schema version
--
--  RETURNS
--      The current version as a result set
--
CREATE PROCEDURE dbo.SelectCurrentSchemaVersion
AS
BEGIN
    SET NOCOUNT ON

    SELECT MAX(Version)
    FROM SchemaVersion
    WHERE Status = 'complete'
END
GO

--
--  STORED PROCEDURE
--      UpsertSchemaVersion
--
--  DESCRIPTION
--      Creates or updates a new schema version entry
--
--  PARAMETERS
--      @version
--          * The version number
--      @status
--          * The status of the version
--
CREATE PROCEDURE dbo.UpsertSchemaVersion
    @version int,
    @status varchar(10)
AS
    SET NOCOUNT ON

    IF EXISTS(SELECT *
        FROM dbo.SchemaVersion
        WHERE Version = @version)
    BEGIN
        UPDATE dbo.SchemaVersion
        SET Status = @status
        WHERE Version = @version
    END
    ELSE
    BEGIN
        INSERT INTO dbo.SchemaVersion
            (Version, Status)
        VALUES
            (@version, @status)
    END
GO


/*************************************************************
    Model tables
**************************************************************/

CREATE TABLE dbo.SearchParam
(
    SearchParamId smallint IDENTITY(1,1) NOT NULL,
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL
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
    RawResource varbinary(max) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.Resource
(
    ResourceSurrogateId
)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)

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

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsDeleted = 0

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

CREATE TYPE dbo.ResourceWriteClaimTableType_1 AS TABLE
(
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

CREATE TYPE dbo.CompartmentAssignmentTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_CompartmentAssignment
ON dbo.CompartmentAssignment
(
    ResourceSurrogateId,
    CompartmentTypeId,
    ReferenceResourceId
)

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
ON dbo.CompartmentAssignment
(
    CompartmentTypeId,
    ReferenceResourceId
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Reference Search Param
**************************************************************/

CREATE TYPE dbo.ReferenceSearchParamTableType_1 AS TABLE
(
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NOT NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL
)

CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId smallint NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId smallint NOT NULL,
    ReferenceResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion int NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion
ON dbo.ReferenceSearchParam
(
    SearchParamId,
    ReferenceResourceId,
    ReferenceResourceTypeId,
    BaseUri
)
INCLUDE
(
    ResourceTypeId,
    ReferenceResourceVersion
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token Search Param
**************************************************************/

CREATE TYPE dbo.TokenSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
ON dbo.TokenSearchParam
(
    SearchParamId,
    Code,
    SystemId
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token Text
**************************************************************/

CREATE TYPE dbo.TokenTextTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    SearchParamId,
    Text
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    String Search Param
**************************************************************/

CREATE TYPE dbo.StringSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_Text
ON dbo.StringSearchParam
(
    SearchParamId,
    Text
)
INCLUDE
(
    ResourceTypeId,
    TextOverflow -- workaround for https://support.microsoft.com/en-gb/help/3051225/a-filtered-index-that-you-create-together-with-the-is-null-predicate-i
)
WHERE IsHistory = 0 AND TextOverflow IS NULL
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON dbo.StringSearchParam
(
    SearchParamId,
    Text
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0 AND TextOverflow IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    URI Search Param
**************************************************************/

CREATE TYPE dbo.UriSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_UriSearchParam
ON dbo.UriSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_UriSearchParam_SearchParamId_Uri
ON dbo.UriSearchParam
(
    SearchParamId,
    Uri
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO


/*************************************************************
    Number Search Param
**************************************************************/

-- We support the underlying value being a range, though we expect the vast majority of entries to be a single value.
-- Either:
--  (1) SingleValue is not null and LowValue and HighValue are both null, or
--  (2) SingleValue is null and LowValue and HighValue are both not null
-- We make use of filtered nonclustered indexes to keep queries over the ranges limited to those rows that actually have ranges

CREATE TYPE dbo.NumberSearchParamTableType_1 AS TABLE
(
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

CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    SingleValue
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    LowValue,
    HighValue
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    HighValue,
    LowValue
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL

GO

/*************************************************************
    Quantity Search Param
**************************************************************/

-- See comment above for number search params for how we store ranges

CREATE TYPE dbo.QuantitySearchParamTableType_1 AS TABLE
(
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

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
ON dbo.QuantitySearchParam
(
    SearchParamId,
    QuantityCodeId,
    SingleValue
)
INCLUDE
(
    ResourceTypeId,
    SystemId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
ON dbo.QuantitySearchParam
(
    SearchParamId,
    QuantityCodeId,
    LowValue,
    HighValue
)
INCLUDE
(
    ResourceTypeId,
    SystemId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL

CREATE NONCLUSTERED INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
ON dbo.QuantitySearchParam
(
    SearchParamId,
    QuantityCodeId,
    HighValue,
    LowValue
)
INCLUDE
(
    ResourceTypeId,
    SystemId
)
WHERE IsHistory = 0 AND LowValue IS NOT NULL

GO

/*************************************************************
    Date Search Param
**************************************************************/

CREATE TYPE dbo.DateTimeSearchParamTableType_1 AS TABLE
(
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

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    StartDateTime,
    EndDateTime
)
INCLUDE
(
    ResourceTypeId,
    IsLongerThanADay
)
WHERE IsHistory = 0

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    EndDateTime,
    StartDateTime
)
INCLUDE
(
    ResourceTypeId,
    IsLongerThanADay
)
WHERE IsHistory = 0


CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    StartDateTime,
    EndDateTime
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    EndDateTime,
    StartDateTime
)
INCLUDE
(
    ResourceTypeId
)
WHERE IsHistory = 0 AND IsLongerThanADay = 1

GO

/*************************************************************
    Reference$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.ReferenceTokenCompositeSearchParamTableType_1 AS TABLE
(
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NOT NULL,
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
    ReferenceResourceTypeId1 smallint NOT NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2
ON dbo.ReferenceTokenCompositeSearchParam
(
    SearchParamId,
    ReferenceResourceId1,
    Code2
)
INCLUDE
(
    ResourceTypeId,
    ReferenceResourceTypeId1,
    BaseUri1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)


CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ResourceTypeId_ReferenceResourceTypeId_ReferenceResourceId
ON dbo.ReferenceSearchParam
(
	SearchParamId,
	ResourceTypeId,
	ReferenceResourceTypeId,
	ReferenceResourceId
)
INCLUDE
(
    ReferenceResourceVersion,
	BaseUri
)
WHERE IsHistory = 0

GO

/*************************************************************
    Token$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.TokenTokenCompositeSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
ON dbo.TokenTokenCompositeSearchParam
(
    SearchParamId,
    Code1,
    Code2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token$DateTime Composite Search Param
**************************************************************/

CREATE TYPE dbo.TokenDateTimeCompositeSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1,
    IsLongerThanADay2
)

WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1,
    IsLongerThanADay2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1
)

WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long
ON dbo.TokenDateTimeCompositeSearchParam
(
    SearchParamId,
    Code1,
    EndDateTime2,
    StartDateTime2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1
)
WHERE IsHistory = 0 AND IsLongerThanADay2 = 1
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token$Quantity Composite Search Param
**************************************************************/

CREATE TYPE dbo.TokenQuantityCompositeSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    SearchParamId,
    Code1,
    SingleValue2
)
INCLUDE
(
    ResourceTypeId,
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND SingleValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2
)
INCLUDE
(
    ResourceTypeId,
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
ON dbo.TokenQuantityCompositeSearchParam
(
    SearchParamId,
    Code1,
    HighValue2,
    LowValue2
)
INCLUDE
(
    ResourceTypeId,
    QuantityCodeId2,
    SystemId1,
    SystemId2
)
WHERE IsHistory = 0 AND LowValue2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token$String Composite Search Param
**************************************************************/

CREATE TYPE dbo.TokenStringCompositeSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
ON dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenStringCompositeSearchParam
(
    SearchParamId,
    Code1,
    Text2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1,
    TextOverflow2 -- workaround for https://support.microsoft.com/en-gb/help/3051225/a-filtered-index-that-you-create-together-with-the-is-null-predicate-i
)
WHERE IsHistory = 0 AND TextOverflow2 IS NULL
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow
ON dbo.TokenStringCompositeSearchParam
(
    SearchParamId,
    Code1,
    Text2
)
INCLUDE
(
    ResourceTypeId,
    SystemId1
)
WHERE IsHistory = 0 AND TextOverflow2 IS NOT NULL
WITH (DATA_COMPRESSION = PAGE)

GO


/*************************************************************
    Token$Number$Number Composite Search Param
**************************************************************/

-- See number search param for how we deal with null. We apply a similar pattern here,
-- except that we pass in a HasRange bit though the TVP. The alternative would have
-- for a computed column, but a computed column cannot be used in as a index filter
-- (even if it is a persisted computed column).


CREATE TYPE dbo.TokenNumberNumberCompositeSearchParamTableType_1 AS TABLE
(
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
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenNumberNumberCompositeSearchParam
(
    SearchParamId,
    Code1,
    SingleValue2,
    SingleValue3
)
INCLUDE
(
    ResourceTypeId,
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 0
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
ON dbo.TokenNumberNumberCompositeSearchParam
(
    SearchParamId,
    Code1,
    LowValue2,
    HighValue2,
    LowValue3,
    HighValue3
)
INCLUDE
(
    ResourceTypeId,
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 1
WITH (DATA_COMPRESSION = PAGE)

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
--     UpsertResource
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
--     @resourceid
--         * The resource ID (must be the same as the in the resource itself)
--     @allowCreate
--         * If false, an error is thrown if the resource does not already exist
--     @isDeleted
--         * Whether this resource marks the resource as deleted
--     @updatedDateTime
--         * The last modified time in the resource
--     @keepHistory
--         * Whether the existing version of the resource should be preserved
--     @requestMethod
--         * The HTTP method/verb used for the request
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
CREATE PROCEDURE dbo.UpsertResource
    @baseResourceSurrogateId bigint,
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @keepHistory bit,
    @requestMethod varchar(10),
    @rawResource varbinary(max),
    @resourceWriteClaims dbo.ResourceWriteClaimTableType_1 READONLY,
    @compartmentAssignments dbo.CompartmentAssignmentTableType_1 READONLY,
    @referenceSearchParams dbo.ReferenceSearchParamTableType_1 READONLY,
    @tokenSearchParams dbo.TokenSearchParamTableType_1 READONLY,
    @tokenTextSearchParams dbo.TokenTextTableType_1 READONLY,
    @stringSearchParams dbo.StringSearchParamTableType_1 READONLY,
    @numberSearchParams dbo.NumberSearchParamTableType_1 READONLY,
    @quantitySearchParams dbo.QuantitySearchParamTableType_1 READONLY,
    @uriSearchParams dbo.UriSearchParamTableType_1 READONLY,
    @dateTimeSearchParms dbo.DateTimeSearchParamTableType_1 READONLY,
    @referenceTokenCompositeSearchParams dbo.ReferenceTokenCompositeSearchParamTableType_1 READONLY,
    @tokenTokenCompositeSearchParams dbo.TokenTokenCompositeSearchParamTableType_1 READONLY,
    @tokenDateTimeCompositeSearchParams dbo.TokenDateTimeCompositeSearchParamTableType_1 READONLY,
    @tokenQuantityCompositeSearchParams dbo.TokenQuantityCompositeSearchParamTableType_1 READONLY,
    @tokenStringCompositeSearchParams dbo.TokenStringCompositeSearchParamTableType_1 READONLY,
    @tokenNumberNumberCompositeSearchParams dbo.TokenNumberNumberCompositeSearchParamTableType_1 READONLY
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
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            -- Set the indexes for this resource as history.
            -- Note there is no IsHistory column on ResourceWriteClaim since we do not query it.

            UPDATE dbo.CompartmentAssignment
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenText
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.StringSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.UriSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.NumberSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.QuantitySearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.DateTimeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.ReferenceTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenTokenCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenDateTimeCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenQuantityCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenStringCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            UPDATE dbo.TokenNumberNumberCompositeSearchParam
            SET IsHistory = 1
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

        END
        ELSE BEGIN

            -- Not keeping history. Delete the current resource and all associated indexes.

            DELETE FROM dbo.Resource
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ResourceWriteClaim
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.CompartmentAssignment
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenText
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.StringSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.UriSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.NumberSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.QuantitySearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.DateTimeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.ReferenceTokenCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenTokenCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenDateTimeCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenQuantityCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenStringCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

            DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
            WHERE ResourceSurrogateId = @previousResourceSurrogateId

        END
    END

    DECLARE @resourceSurrogateId bigint = @baseResourceSurrogateId + (NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence)

    INSERT INTO dbo.Resource
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource)
    VALUES
        (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, @isDeleted, @requestMethod, @rawResource)

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
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0
    END
    ELSE BEGIN
        SELECT ResourceSurrogateId, Version, IsDeleted, IsHistory, RawResource
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND Version = @version
    END
GO

--
-- STORED PROCEDURE
--     Reads a single resource
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
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenText
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.StringSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.UriSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.NumberSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.QuantitySearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.DateTimeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.ReferenceTokenCompositeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenTokenCompositeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenDateTimeCompositeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenQuantityCompositeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenStringCompositeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

    DELETE FROM dbo.TokenNumberNumberCompositeSearchParam
    WHERE ResourceSurrogateId IN (SELECT ResourceSurrogateId FROM @resourceSurrogateIds)

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
  
    SELECT MIN_ACTIVE_ROWVERSION()

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
    Search Parameter Registry
**************************************************************/

CREATE TYPE dbo.SearchParamRegistryTableType_1 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    IsPartiallySupported bit NOT NULL
)

CREATE TABLE dbo.SearchParamRegistry
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(10) NOT NULL,
    LastUpdated datetimeoffset(7) NULL,
    IsPartiallySupported bit NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_SearchParamRegistry
ON dbo.SearchParamRegistry
(
    Uri
)

GO

/*************************************************************
    Stored procedures for the search parameter registry
**************************************************************/
--
-- STORED PROCEDURE
--     Gets all the search parameters and their statuses.
--
-- DESCRIPTION
--     Retrieves and returns the contents of the search parameter registry.
--
-- RETURN VALUE
--     The search parameters and their statuses.
--
CREATE PROCEDURE dbo.GetSearchParamStatuses
AS
    SET NOCOUNT ON
    
    SELECT * FROM dbo.SearchParamRegistry
GO

--
-- STORED PROCEDURE
--     Given a table of search parameters, upserts the registry.
--
-- DESCRIPTION
--     If a parameter with a matching URI already exists in the registry, it is updated.
--     If not, a new entry is created.
--
-- PARAMETERS
--     @searchParamStatuses
--         * The updated or new search parameter statuses
--
CREATE PROCEDURE dbo.UpsertSearchParamStatus
    @searchParamStatuses dbo.SearchParamRegistryTableType_1 READONLY
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE
    BEGIN TRANSACTION

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()

    -- Acquire and hold an exclusive table lock for the entire transaction to prevent parameters from being added or modified during upsertion.
    UPDATE dbo.SearchParamRegistry
    WITH (TABLOCKX)
    SET Status = sps.Status, LastUpdated = @lastUpdated, IsPartiallySupported = sps.IsPartiallySupported
    FROM dbo.SearchParamRegistry INNER JOIN @searchParamStatuses as sps
    ON dbo.SearchParamRegistry.Uri = sps.Uri

    INSERT INTO dbo.SearchParamRegistry
        (Uri, Status, LastUpdated, IsPartiallySupported)
    SELECT sps.Uri, sps.Status, @lastUpdated, sps.IsPartiallySupported
    FROM @searchParamStatuses AS sps
    WHERE sps.Uri NOT IN
        (SELECT Uri FROM dbo.SearchParamRegistry) 

    COMMIT TRANSACTION
GO

--
-- STORED PROCEDURE
--     Counts the number of search parameters.
--
-- DESCRIPTION
--     Retrieves and returns the number of rows in the search parameter registry.
--
-- RETURN VALUE
--     The number of search parameters in the registry.
--
CREATE PROCEDURE dbo.GetSearchParamRegistryCount
AS
    SET NOCOUNT ON
    
    SELECT COUNT(*) FROM dbo.SearchParamRegistry
GO

--
-- STORED PROCEDURE
--     Inserts a search parameter and its status into the search parameter registry.
--
-- DESCRIPTION
--     Adds a row to the search parameter registry. This is intended to be called within
--     a transaction that also queries if the table is empty and needs to be initialized.
--
-- PARAMETERS
--     @searchParamStatuses
--         * The updated search parameter statuses
--
CREATE PROCEDURE dbo.InsertIntoSearchParamRegistry
    @searchParamStatuses dbo.SearchParamRegistryTableType_1 READONLY
AS
    SET NOCOUNT ON

    DECLARE @lastUpdated datetimeoffset(7) = SYSDATETIMEOFFSET()
    
    INSERT INTO dbo.SearchParamRegistry
        (Uri, Status, LastUpdated, IsPartiallySupported)
    SELECT sps.Uri, sps.Status, @lastUpdated, sps.IsPartiallySupported
    FROM @searchParamStatuses AS sps
GO
