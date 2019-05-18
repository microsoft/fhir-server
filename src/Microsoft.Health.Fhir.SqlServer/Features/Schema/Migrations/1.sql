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
ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON

-- Avoid blocking queries when statistics need to be rebuilt
ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS_ASYNC ON
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
    (1, 'started')

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
    Uri varchar(128) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_SearchParam ON dbo.SearchParam
(
    Uri
)

CREATE TABLE dbo.ResourceType
(
    ResourceTypeId smallint IDENTITY(1,1) NOT NULL,
    Name nvarchar(50) NOT NULL
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
    Value nvarchar(256) NOT NULL
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
    ResourceId varchar(64) NOT NULL,
    Version int NOT NULL,
    IsHistory bit NOT NULL,
    ResourceSurrogateId bigint NOT NULL,
    LastUpdated datetime2(7) NOT NULL,
    IsDeleted bit NOT NULL,
    RequestMethod varchar(10) NULL,
    RawResource varbinary(max) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.Resource (
    ResourceSurrogateId
)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource (
    ResourceTypeId,
    ResourceId,
    Version
)

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId ON dbo.Resource (
    ResourceTypeId,
    ResourceId
)
INCLUDE (Version)
WHERE IsHistory = 0

/*************************************************************
    Capture claims on write
**************************************************************/

CREATE TABLE dbo.ClaimType
(
    ClaimTypeId tinyint IDENTITY(1,1) NOT NULL,
    Name varchar(128) NOT NULL
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

CREATE CLUSTERED INDEX IXC_LastModifiedClaim on dbo.ResourceWriteClaim
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
    Name varchar(128) NOT NULL
)

CREATE UNIQUE CLUSTERED INDEX IXC_CompartmentType on dbo.CompartmentType
(
    Name
)

CREATE TYPE dbo.CompartmentAssignmentTableType_1 AS TABLE  
(
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) NOT NULL
)

CREATE TABLE dbo.CompartmentAssignment
(
    ResourceSurrogateId bigint NOT NULL,
    CompartmentTypeId tinyint NOT NULL,
    ReferenceResourceId varchar(64) NOT NULL,
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
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Reference Search Param
**************************************************************/

CREATE TYPE dbo.ReferenceSearchParamTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) NULL,
    ReferenceResourceTypeId smallint NOT NULL,
    ReferenceResourceId varchar(64) NOT NULL,
    ReferenceResourceVersion int NULL
)

CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri varchar(128) NULL,
    ReferenceResourceTypeId smallint NOT NULL,
    ReferenceResourceId varchar(64) NOT NULL,
    ReferenceResourceVersion int NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    ReferenceResourceId,
    ReferenceResourceTypeId,
    BaseUri,
    ReferenceResourceVersion
)

CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion
ON dbo.ReferenceSearchParam
(
    SearchParamId,
    ReferenceResourceTypeId,
    ReferenceResourceId,
    BaseUri,
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
    Code varchar(128) NOT NULL
)

CREATE TABLE dbo.TokenSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(128) NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Code,
    SystemId
)

CREATE NONCLUSTERED INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId 
ON dbo.TokenSearchParam
(
    SearchParamId,
    Code,
    SystemId
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
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory bit NOT NULL
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceSurrogateId,
    SearchParamId,
    Text
)

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    SearchParamId,
    Text
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
    TextOverflow -- workaround for https://support.microsoft.com/en-gb/help/3051225/a-filtered-index-that-you-create-together-with-the-is-null-predicate-i
)
WHERE IsHistory = 0 AND TextOverflow IS NULL
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWhereNoOverflow
ON dbo.StringSearchParam
(
    SearchParamId,
    Text
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
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    IsHistory bit NOT NULL
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_UriSearchParam
ON dbo.UriSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Uri
)

CREATE NONCLUSTERED INDEX IX_UriSearchParam_SearchParamId_Uri
ON dbo.UriSearchParam
(
    SearchParamId,
    Uri
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
    SearchParamId,
    SingleValue
)

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    SingleValue
) 
WHERE IsHistory = 0 AND SingleValue IS NOT NULL

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    LowValue,
    HighValue
) 
WHERE IsHistory = 0 AND LowValue IS NOT NULL

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
ON dbo.NumberSearchParam
(
    SearchParamId,
    HighValue,
    LowValue
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
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    QuantityCodeId int NULL,
    SingleValue decimal(18,6) NULL,
    LowValue decimal(18,6) SPARSE NULL,
    HighValue decimal(18,6) SPARSE NULL,
    IsHistory bit NOT NULL
)

CREATE CLUSTERED INDEX QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    QuantityCodeId,
    SingleValue
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
    EndDateTime datetimeoffset(7) NOT NULL
)

CREATE TABLE dbo.DateTimeSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetime2(7) NOT NULL,
    EndDateTime datetime2(7) NOT NULL,
    IsHistory bit NOT NULL
)

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    StartDateTime,
    EndDateTime
)

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    StartDateTime,
    EndDateTime
) 
WHERE IsHistory = 0

CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime
ON dbo.DateTimeSearchParam
(
    SearchParamId,
    EndDateTime,
    StartDateTime
) 
WHERE IsHistory = 0

GO

/*************************************************************
    Reference$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.ReferenceTokenCompositeSearchParamTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) NULL,
    ReferenceResourceTypeId1 smallint NOT NULL,
    ReferenceResourceId1 varchar(64) NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) NOT NULL
)

CREATE TABLE dbo.ReferenceTokenCompositeSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) NULL,
    ReferenceResourceTypeId1 smallint NOT NULL,
    ReferenceResourceId1 varchar(64) NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(128) NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    ReferenceResourceId1,
    Code2
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
    ReferenceResourceTypeId1,
    BaseUri1,
    SystemId2
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.TokenTokenCompositeSearchParamTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(128) NOT NULL
)

CREATE TABLE dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    SystemId2 int NULL,
    Code2 varchar(128) NOT NULL,
    IsHistory bit NOT NULL
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Code1,
    Code2
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
    Code1 varchar(128) NOT NULL,
    StartDateTime2 datetimeoffset(7) NOT NULL,
    EndDateTime2 datetimeoffset(7) NOT NULL
)

CREATE TABLE dbo.TokenDateTimeCompositeSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    StartDateTime2 datetime2(7) NOT NULL,
    EndDateTime2 datetime2(7) NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Code1,
    StartDateTime2,
    EndDateTime2
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
    SystemId1
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
    SystemId1
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Token$Quantity Composite Search Param
**************************************************************/

CREATE TYPE dbo.TokenQuantityCompositeSearchParamTableType_1 AS TABLE  
(
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    SystemId2 int NULL,
    QuantityCodeId2 int NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL
)

CREATE TABLE dbo.TokenQuantityCompositeSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
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
    SearchParamId,
    Code1,
    SingleValue2
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
    Code1 varchar(128) NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NOT NULL
)

CREATE TABLE dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_CI_AI NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_CI_AI NOT NULL,
    IsHistory bit NOT NULL,
) WITH (DATA_COMPRESSION = PAGE)

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
ON dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId,
    Code1,
    Text2
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
    SystemId1,
    TextOverflow2 -- workaround for https://support.microsoft.com/en-gb/help/3051225/a-filtered-index-that-you-create-together-with-the-is-null-predicate-i
)
WHERE IsHistory = 0 AND TextOverflow2 IS NULL
WITH (DATA_COMPRESSION = PAGE)

CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2_WithOverflow
ON dbo.TokenStringCompositeSearchParam
(
    SearchParamId,
    Code1,
    Text2
)
INCLUDE
(
    SystemId1
)
WHERE IsHistory = 0 AND TextOverflow2 IS  NULL
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
    Code1 varchar(128) NOT NULL,
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
    ResourceSurrogateId bigint NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(128) NOT NULL,
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
    SearchParamId,
    Code1,
    SingleValue2,
    SingleValue3
)

CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
ON dbo.TokenNumberNumberCompositeSearchParam
(
    SearchParamId,
    Code1,
    SingleValue2
)
INCLUDE
(
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
    SystemId1
)
WHERE IsHistory = 0 AND HasRange = 1
WITH (DATA_COMPRESSION = PAGE)

GO

/*************************************************************
    Sequence for generating surrogate IDs for resources
**************************************************************/

CREATE SEQUENCE dbo.ResourceSurrogateIdSequence
        AS BIGINT
        START WITH 0
        INCREMENT BY 1
        NO CYCLE
        CACHE 50
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
    @resourceTypeId smallint,
    @resourceId varchar(64),
    @eTag int = NULL,
    @allowCreate bit,
    @isDeleted bit,
    @updatedDateTime datetimeoffset(7),
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

    DECLARE @previousVersion TABLE(
        ResourceSurrogateId bigint NOT NULL,
        Version int NOT NULL);

    if (@keepHistory = 1) BEGIN
        -- Preserve the existing version, marking it as history
        UPDATE dbo.Resource WITH (UPDLOCK, HOLDLOCK)
        SET IsHistory = 1
        OUTPUT inserted.ResourceSurrogateId,
                inserted.Version
        INTO @previousVersion
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0 AND (@isDeleted = 0 OR IsDeleted = 0)
    END
    ELSE BEGIN
        -- Delete the previous version
        DELETE FROM dbo.Resource WITH (UPDLOCK, HOLDLOCK)
        OUTPUT deleted.ResourceSurrogateId,
                deleted.Version
        INTO @previousVersion
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0 AND (@isDeleted = 0 OR IsDeleted = 0)
    END

    DECLARE @version int;

    if (@@ROWCOUNT = 0) BEGIN
        IF (@isDeleted = 1) BEGIN
            -- Either a previous version does not exist or it is already an "IsDeleted" version
            COMMIT TRANSACTION
            RETURN
        END

        IF (@allowCreate = 0) BEGIN
            THROW 50404, 'Resource does not exist and create is not allowed', 1;
        END

        SET @version = 1
    END
    ELSE BEGIN
        -- There is a previous version
        DECLARE @previousResourceSurrogateId bigint
        
        SELECT @version = (Version + 1), @previousResourceSurrogateId = ResourceSurrogateId 
        FROM @previousVersion

        IF (@keepHistory = 1) BEGIN

            -- note there is no IsHistory column on ResourceWriteClaim since we do not query it

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


    IF (@etag IS NOT NULL AND @etag <> (@version - 1)) BEGIN
        THROW 50412, 'Precondition failed', 1;
    END

    DECLARE @resourceSurrogateId bigint = NEXT VALUE FOR dbo.ResourceSurrogateIdSequence

    INSERT INTO dbo.Resource
        (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, LastUpdated, IsDeleted, RequestMethod, RawResource)
    VALUES
        (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, CONVERT(datetime2(7), @updatedDateTime), @isDeleted, @requestMethod, @rawResource)

    INSERT INTO dbo.ResourceWriteClaim 
        (ResourceSurrogateId, ClaimTypeId, ClaimValue)
    SELECT @resourceSurrogateId, ClaimTypeId, ClaimValue 
    FROM @resourceWriteClaims

    INSERT INTO dbo.CompartmentAssignment
        (ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, CompartmentTypeId, ReferenceResourceId, 0
    FROM @compartmentAssignments

    INSERT INTO dbo.ReferenceSearchParam
        (ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, 0
    FROM @referenceSearchParams

    INSERT INTO dbo.TokenSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId, Code, 0
    FROM @tokenSearchParams

    INSERT INTO dbo.TokenText
        (ResourceSurrogateId, SearchParamId, Text, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, Text, 0
    FROM @tokenTextSearchParams

    INSERT INTO dbo.StringSearchParam
        (ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, Text, TextOverflow, 0
    FROM @stringSearchParams

    INSERT INTO dbo.UriSearchParam
        (ResourceSurrogateId, SearchParamId, Uri, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, Uri, 0
    FROM @uriSearchParams

    INSERT INTO dbo.NumberSearchParam
        (ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, 0
    FROM @numberSearchParams

    INSERT INTO dbo.QuantitySearchParam
        (ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, 0
    FROM @quantitySearchParams

    INSERT INTO dbo.DateTimeSearchParam
        (ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, 0
    FROM @dateTimeSearchParms

    INSERT INTO dbo.ReferenceTokenCompositeSearchParam
        (ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, 0
    FROM @referenceTokenCompositeSearchParams

    INSERT INTO dbo.TokenTokenCompositeSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, 0
    FROM @tokenTokenCompositeSearchParams

    INSERT INTO dbo.TokenDateTimeCompositeSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, 0
    FROM @tokenDateTimeCompositeSearchParams

    INSERT INTO dbo.TokenQuantityCompositeSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, 0
    FROM @tokenQuantityCompositeSearchParams

    INSERT INTO dbo.TokenStringCompositeSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, 0
    FROM @tokenStringCompositeSearchParams

    INSERT INTO dbo.TokenNumberNumberCompositeSearchParam
        (ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
    SELECT DISTINCT @resourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, 0
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
        SELECT Version, LastUpdated, IsDeleted, IsHistory, RawResource 
        FROM dbo.Resource
        WHERE ResourceTypeId = @resourceTypeId AND ResourceId = @resourceId AND IsHistory = 0
    END
    ELSE BEGIN
        SELECT Version, LastUpdated, IsDeleted, IsHistory, RawResource 
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
