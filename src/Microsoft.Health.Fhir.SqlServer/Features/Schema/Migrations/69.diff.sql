
-- ReferenceSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceSearchParam') AND name = 'IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId')
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

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceSearchParam') AND name = 'IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion')
  DROP INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion ON dbo.ReferenceSearchParam

-- DateTimeSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax')
	CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
	ON dbo.DateTimeSearchParam
	(
		SearchParamId,
		StartDateTime,
		EndDateTime -- TODO: Should it be in INCLUDE?
	)
	INCLUDE
	(
		IsLongerThanADay,
		IsMin,
		IsMax
	)
    WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime')
  DROP INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime ON dbo.DateTimeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax')
	CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
	ON dbo.DateTimeSearchParam
	(
		SearchParamId,
		EndDateTime,
		StartDateTime -- TODO: Should it be in INCLUDE?
	)
	INCLUDE
	(
		IsLongerThanADay,
		IsMin,
		IsMax
	)
    WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime')
  DROP INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime ON dbo.DateTimeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1')
	CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
	ON dbo.DateTimeSearchParam
	(
		SearchParamId,
		StartDateTime,
		EndDateTime -- TODO: Should it be in INCLUDE?
	)
	INCLUDE
	(
		IsMin,
		IsMax
	)
	WHERE IsLongerThanADay = 1
    WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long')
  DROP INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long ON dbo.DateTimeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1')
	CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
	ON dbo.DateTimeSearchParam
	(
		SearchParamId,
		EndDateTime,
		StartDateTime -- TODO: Should it be in INCLUDE?
	)
	INCLUDE
	(
		IsMin,
		IsMax
	)
	WHERE IsLongerThanADay = 1
    WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam') AND name = 'IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long')
  DROP INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long ON dbo.DateTimeSearchParam

-- NumberSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam') AND name = 'IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL')
	CREATE INDEX IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL
	ON dbo.NumberSearchParam
	(
		SearchParamId,
		SingleValue
	)
	WHERE SingleValue IS NOT NULL
    WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam') AND name = 'IX_NumberSearchParam_SearchParamId_SingleValue')
  DROP INDEX IX_NumberSearchParam_SearchParamId_SingleValue ON dbo.NumberSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam') AND name = 'IX_SearchParamId_LowValue_HighValue')
	CREATE INDEX IX_SearchParamId_LowValue_HighValue
	ON dbo.NumberSearchParam
	(
		SearchParamId,
		LowValue,
		HighValue
	)
	WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam') AND name = 'IX_NumberSearchParam_SearchParamId_LowValue_HighValue')
  DROP INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue ON dbo.NumberSearchParam


IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam') AND name = 'IX_SearchParamId_HighValue_LowValue')
	CREATE INDEX IX_SearchParamId_HighValue_LowValue
	ON dbo.NumberSearchParam
	(
		SearchParamId,
		HighValue,
		LowValue
	)
	WITH (ONLINE = ON)
	ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam') AND name = 'IX_NumberSearchParam_SearchParamId_HighValue_LowValue')
  DROP INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue ON dbo.NumberSearchParam


--QuantitySearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam') AND name = 'IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL')
    CREATE INDEX IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL
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
    WHERE SingleValue IS NOT NULL
    WITH (ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue')
  DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue ON dbo.QuantitySearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam') AND name = 'IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId')
    CREATE INDEX IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId
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
    WITH (ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue')
  DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue ON dbo.QuantitySearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam') AND name = 'IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId')
    CREATE INDEX IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId
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
    WITH (ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue')
  DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue ON dbo.QuantitySearchParam

--ReferenceTokenCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceTokenCompositeSearchParam') AND name = 'IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2')
    CREATE INDEX IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceTokenCompositeSearchParam') AND name = 'IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2')
  DROP INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2 ON dbo.ReferenceTokenCompositeSearchParam

--StringSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_StringSearchParam_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax')
    CREATE INDEX IX_StringSearchParam_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax
    ON dbo.StringSearchParam
    (
        SearchParamId,
        Text
    )
    INCLUDE
    (
        TextOverflow, -- will not be needed when all servers are targeting at least this version. TODO: What?
        IsMin,
        IsMax
    )
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_StringSearchParam_SearchParamId_Text')
  DROP INDEX IX_StringSearchParam_SearchParamId_Text ON dbo.StringSearchParam


IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL')
    CREATE INDEX IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL
    ON dbo.StringSearchParam
    (
        SearchParamId,
        Text
    )
    INCLUDE
    (
        IsMin,
        IsMax
    )
    WHERE TextOverflow IS NOT NULL
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam') AND name = 'IX_StringSearchParam_SearchParamId_TextWithOverflow')
  DROP INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow ON dbo.StringSearchParam

--TokenDateTimeCompositeSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2')
    CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
    ON dbo.TokenDateTimeCompositeSearchParam
    (
        SearchParamId,
        Code1,
        StartDateTime2,
        EndDateTime2
    )
    INCLUDE
    (
        SystemId1,
        IsLongerThanADay2
    )
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2')
  DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2 ON dbo.TokenDateTimeCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2')
    CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
    ON dbo.TokenDateTimeCompositeSearchParam
    (
        SearchParamId,
        Code1,
        EndDateTime2,
        StartDateTime2
    )
    INCLUDE
    (
        SystemId1,
        IsLongerThanADay2
    )
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2')
  DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2 ON dbo.TokenDateTimeCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1')
    CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
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
    WHERE IsLongerThanADay2 = 1
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long')
  DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long ON dbo.TokenDateTimeCompositeSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1')
    CREATE INDEX IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
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
    WHERE IsLongerThanADay2 = 1
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long')
  DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long ON dbo.TokenDateTimeCompositeSearchParam

--TokenNumberNumberCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_SingleValue2_SingleValue3_INCLUDE_SystemId1_WHERE_HasRange_0')
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam') AND name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2')
  DROP INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2 ON dbo.TokenNumberNumberCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3_INCLUDE_SystemId1_WHERE_HasRange_1')
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam') AND name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3')
  DROP INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3 ON dbo.TokenNumberNumberCompositeSearchParam

--TokenQuantityCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_SingleValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_SingleValue2_NOT_NULL')
    CREATE INDEX IX_SearchParamId_Code1_SingleValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_SingleValue2_NOT_NULL
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
    WHERE SingleValue2 IS NOT NULL
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2')
  DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2 ON dbo.TokenQuantityCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_LowValue2_HighValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL')
    CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam
    (
        SearchParamId,
        Code1,
        LowValue2,
        HighValue2 -- TODO: Do we need this as key column?
    )
    INCLUDE
    (
        QuantityCodeId2,
        SystemId1,
        SystemId2
    )
    WHERE LowValue2 IS NOT NULL
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2')
  DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2 ON dbo.TokenQuantityCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_HighValue2_LowValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL')
    CREATE INDEX IX_SearchParamId_Code1_HighValue2_LowValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
    ON dbo.TokenQuantityCompositeSearchParam
    (
        SearchParamId,
        Code1,
        HighValue2,
        LowValue2 -- TODO: Do we need this as key column?
    )
    INCLUDE
    (
        QuantityCodeId2,
        SystemId1,
        SystemId2
    )
    WHERE LowValue2 IS NOT NULL
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2')
  DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2 ON dbo.TokenQuantityCompositeSearchParam

--TokenStringCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_TextOverflow2')
    CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_TextOverflow2
    ON dbo.TokenStringCompositeSearchParam
    (
        SearchParamId,
        Code1,
        Text2
    )
    INCLUDE
    (
        SystemId1,
        TextOverflow2 -- will not be needed when all servers are targeting at least this version. TODO: What?
    )
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam') AND name = 'IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2')
  DROP INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2 ON dbo.TokenStringCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_WHERE_TextOverflow2_NOT_NULL')
    CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_WHERE_TextOverflow2_NOT_NULL
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
    WHERE TextOverflow2 IS NOT NULL
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam') AND name = 'IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow')
  DROP INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow ON dbo.TokenStringCompositeSearchParam

--TokenTokenCompositeSearchParam

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenTokenCompositeSearchParam') AND name = 'IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2')
    CREATE INDEX IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenTokenCompositeSearchParam') AND name = 'IX_TokenTokenCompositeSearchParam_Code1_Code2')
  DROP INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2 ON dbo.TokenTokenCompositeSearchParam

--UriSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('UriSearchParam') AND name = 'IX_SearchParamId_Uri')
    CREATE INDEX IX_SearchParamId_Uri
    ON dbo.UriSearchParam
    (
        SearchParamId,
        Uri
    )
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId)

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('UriSearchParam') AND name = 'IX_UriSearchParam_SearchParamId_Uri')
  DROP INDEX IX_UriSearchParam_SearchParamId_Uri ON dbo.UriSearchParam

--TokenSearchParam
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParam') AND name = 'IX_SearchParamId_Code_INCLUDE_SystemId')
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
    ON PartitionScheme_ResourceTypeId (ResourceTypeId);

IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParam') AND name = 'IX_TokenSeachParam_SearchParamId_Code_SystemId')
  DROP INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId ON dbo.TokenSearchParam
