-- create a global temp table to keep track of progress

IF NOT EXISTS (
    SELECT * 
    FROM sys.tables
    WHERE name = 'SchemaMigrationProgress')
BEGIN
    CREATE TABLE dbo.SchemaMigrationProgress
    (
        Start datetime default CURRENT_TIMESTAMP,
        Message nvarchar(max)
    )

END

GO

CREATE OR ALTER PROCEDURE dbo.LogSchemaMigrationProgress
    @message varchar(max)
AS
    INSERT INTO dbo.SchemaMigrationProgress (Message) VALUES (@message)
GO

EXEC LogSchemaMigrationProgress 'Foo'

select * from SchemaMigrationProgress

IF NOT EXISTS (SELECT *
               FROM  sys.partition_functions
               WHERE name = N'PartitionFunction_ResourceTypeId')
BEGIN
    CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId (smallint) 
    AS RANGE RIGHT FOR VALUES (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150);
END

IF NOT EXISTS (SELECT *
               FROM  sys.partition_schemes
               WHERE name = N'PartitionScheme_ResourceTypeId')
BEGIN
    CREATE PARTITION SCHEME PartitionScheme_ResourceTypeId 
    AS PARTITION PartitionFunction_ResourceTypeId ALL TO ([PRIMARY]);
END

/*************************************************************
    Resource table
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_Resource'

CREATE UNIQUE CLUSTERED INDEX IXC_Resource ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_Resource_ResourceTypeId_ResourceId_Version'

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceId_Version ON dbo.Resource
(
    ResourceTypeId,
    ResourceId,
    Version
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_Resource_ResourceTypeId_ResourceId'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_Resource_ResourceTypeId_ResourceSurrgateId'

CREATE UNIQUE NONCLUSTERED INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON dbo.Resource
(
    ResourceTypeId,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND IsDeleted = 0
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)


/*************************************************************
    Compartments
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_CompartmentAssignment'

CREATE CLUSTERED INDEX IXC_CompartmentAssignment
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    ResourceSurrogateId,
    CompartmentTypeId,
    ReferenceResourceId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId'

CREATE NONCLUSTERED INDEX IX_CompartmentAssignment_CompartmentTypeId_ReferenceResourceId
ON dbo.CompartmentAssignment
(
    ResourceTypeId,
    CompartmentTypeId,
    ReferenceResourceId,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_ReferenceSearchParam'

CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenSearchParam'

CREATE CLUSTERED INDEX IXC_TokenSearchParam
ON dbo.TokenSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenSeachParam_SearchParamId_Code_SystemId'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenText'

CREATE CLUSTERED INDEX IXC_TokenText
ON dbo.TokenText
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenText_SearchParamId_Text'

CREATE NONCLUSTERED INDEX IX_TokenText_SearchParamId_Text
ON dbo.TokenText
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_StringSearchParam'

CREATE CLUSTERED INDEX IXC_StringSearchParam
ON dbo.StringSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_StringSearchParam_SearchParamId_Text'

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
    TextOverflow -- workaround for https://support.microsoft.com/en-gb/help/3051225/a-filtered-index-that-you-create-together-with-the-is-null-predicate-i
)
WHERE IsHistory = 0 AND TextOverflow IS NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_StringSearchParam_SearchParamId_TextWithOverflow'

CREATE NONCLUSTERED INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow
ON dbo.StringSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Text,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND TextOverflow IS NOT NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_UriSearchParam'

CREATE CLUSTERED INDEX IXC_UriSearchParam
ON dbo.UriSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_UriSearchParam_SearchParamId_Uri'

CREATE NONCLUSTERED INDEX IX_UriSearchParam_SearchParamId_Uri
ON dbo.UriSearchParam
(
    ResourceTypeId,
    SearchParamId,
    Uri,
    ResourceSurrogateId
)
WHERE IsHistory = 0
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_NumberSearchParam'

CREATE CLUSTERED INDEX IXC_NumberSearchParam
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_NumberSearchParam_SearchParamId_SingleValue'

CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_SingleValue
ON dbo.NumberSearchParam
(
    ResourceTypeId,
    SearchParamId,
    SingleValue,
    ResourceSurrogateId
)
WHERE IsHistory = 0 AND SingleValue IS NOT NULL
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_NumberSearchParam_SearchParamId_LowValue_HighValue'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_NumberSearchParam_SearchParamId_HighValue_LowValue'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_QuantitySearchParam'

CREATE CLUSTERED INDEX IXC_QuantitySearchParam
ON dbo.QuantitySearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_DateTimeSearchParam'

CREATE CLUSTERED INDEX IXC_DateTimeSearchParam
ON dbo.DateTimeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long'

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
WITH (ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_ReferenceTokenCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_ReferenceTokenCompositeSearchParam
ON dbo.ReferenceTokenCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenTokenCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenTokenCompositeSearchParam
ON dbo.TokenTokenCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenTokenCompositeSearchParam_Code1_Code2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenDateTimeCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenDateTimeCompositeSearchParam
ON dbo.TokenDateTimeCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId,
    ResourceSurrogateId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenQuantityCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenQuantityCompositeSearchParam
ON dbo.TokenQuantityCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenStringCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenStringCompositeSearchParam
ON dbo.TokenStringCompositeSearchParam
(
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2'

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
    TextOverflow2 -- workaround for https://support.microsoft.com/en-gb/help/3051225/a-filtered-index-that-you-create-together-with-the-is-null-predicate-i
)
WHERE IsHistory = 0 AND TextOverflow2 IS NULL
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IXC_TokenNumberNumberCompositeSearchParam'

CREATE CLUSTERED INDEX IXC_TokenNumberNumberCompositeSearchParam
ON dbo.TokenNumberNumberCompositeSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

EXEC dbo.LogSchemaMigrationProgress 'Updating IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3'

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
WITH (DATA_COMPRESSION = PAGE, ONLINE=ON, DROP_EXISTING=ON) 
ON PartitionScheme_ResourceTypeId(ResourceTypeId)

GO
