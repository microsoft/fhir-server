CREATE TYPE dbo.BulkResourceWriteClaimTableType_1 AS TABLE
(
    Offset              int                 NOT NULL,
    ClaimTypeId         tinyint             NOT NULL,
    ClaimValue          nvarchar(128)       NOT NULL
)


CREATE TYPE dbo.BulkCompartmentAssignmentTableType_1 AS TABLE
(
    Offset                      int                 NOT NULL,
    CompartmentTypeId           tinyint             NOT NULL,
    ReferenceResourceId         varchar(64)         COLLATE Latin1_General_100_CS_AS NOT NULL
)

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

/*************************************************************
    Token Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId int NULL,
    Code varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)

/*************************************************************
    Token Text
**************************************************************/

CREATE TYPE dbo.BulkTokenTextTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(400) COLLATE Latin1_General_CI_AI NOT NULL
)

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

/*************************************************************
    String Search Param
**************************************************************/

CREATE TYPE dbo.BulkStringSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
    IsMin bit NOT NULL,
    IsMax bit NOT NULL
)

/*************************************************************
    URI Search Param
**************************************************************/

CREATE TYPE dbo.BulkUriSearchParamTableType_1 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    Uri varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL
)

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

/*************************************************************
    Date Search Param
**************************************************************/

CREATE TYPE dbo.BulkDateTimeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    StartDateTime datetimeoffset(7) NOT NULL,
    EndDateTime datetimeoffset(7) NOT NULL,
    IsLongerThanADay bit NOT NULL,
    IsMin bit NOT NULL,
    IsMax bit NOT NULL
)

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

/*************************************************************
    Reference$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkReferenceTokenCompositeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    BaseUri1 varchar(128) COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId1 smallint NULL,
    ReferenceResourceId1 varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion1 int NULL,
    SystemId2 int NULL,
    Code2 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2 varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)

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

/*************************************************************
    Token$Token Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenTokenCompositeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2 int NULL,
    Code2 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow2 varchar(max) COLLATE Latin1_General_100_CS_AS NULL
)

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

/*************************************************************
    Token$DateTime Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenDateTimeCompositeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    StartDateTime2 datetimeoffset(7) NOT NULL,
    EndDateTime2 datetimeoffset(7) NOT NULL,
    IsLongerThanADay2 bit NOT NULL
)

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

/*************************************************************
    Token$Quantity Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenQuantityCompositeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    SystemId2 int NULL,
    QuantityCodeId2 int NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL
)

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

/*************************************************************
    Token$String Composite Search Param
**************************************************************/

CREATE TYPE dbo.BulkTokenStringCompositeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    Text2 nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
    TextOverflow2 nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL
)

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

/*************************************************************
    Token$Number$Number Composite Search Param
**************************************************************/

-- See number search param for how we deal with null. We apply a similar pattern here,
-- except that we pass in a HasRange bit though the TVP. The alternative would have
-- for a computed column, but a computed column cannot be used in as a index filter
-- (even if it is a persisted computed column).

CREATE TYPE dbo.BulkTokenNumberNumberCompositeSearchParamTableType_2 AS TABLE
(
    Offset int NOT NULL,
    SearchParamId smallint NOT NULL,
    SystemId1 int NULL,
    Code1 varchar(256) COLLATE Latin1_General_100_CS_AS NOT NULL,
    CodeOverflow1 varchar(max) COLLATE Latin1_General_100_CS_AS NULL,
    SingleValue2 decimal(18,6) NULL,
    LowValue2 decimal(18,6) NULL,
    HighValue2 decimal(18,6) NULL,
    SingleValue3 decimal(18,6) NULL,
    LowValue3 decimal(18,6) NULL,
    HighValue3 decimal(18,6) NULL,
    HasRange bit NOT NULL
)

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

CREATE TYPE dbo.SearchParamTableType_2 AS TABLE
(
    Uri varchar(128) COLLATE Latin1_General_100_CS_AS NOT NULL,
    Status varchar(20) NOT NULL,
    IsPartiallySupported bit NOT NULL
)

CREATE TYPE dbo.BulkReindexResourceTableType_1 AS TABLE
(
    Offset int NOT NULL,
    ResourceTypeId smallint NOT NULL,
    ResourceId varchar(64) COLLATE Latin1_General_100_CS_AS NOT NULL,
    ETag int NULL,
    SearchParamHash varchar(64) NOT NULL
)


/*************************************************************
    Resource Bulk Import feature
**************************************************************/
CREATE TYPE dbo.BulkImportResourceType_1 AS TABLE
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
