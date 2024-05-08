BEGIN TRY
  DECLARE @Objects TABLE (Name varchar(100) PRIMARY KEY)
  INSERT INTO @Objects SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam'

  DECLARE @Tbl varchar(100)
         ,@TblTable varchar(100)
         ,@SQL varchar(max)

  WHILE EXISTS (SELECT * FROM @Objects)
  BEGIN
    SET @Tbl = (SELECT TOP 1 Name FROM @Objects)
    SET @TblTable = @Tbl+'_Table'
    SET @SQL = ''

    SELECT TOP 100 @SQL = @SQL + CASE WHEN @SQL <> '' THEN ',' ELSE '' END + CASE WHEN name = 'IsHistory' THEN 'IsHistory = convert(bit,0)' ELSE name END FROM sys.columns WHERE object_id = object_id(@Tbl) ORDER BY column_id
    SET @SQL = 'CREATE VIEW '+@Tbl+' AS SELECT '+@SQL+' FROM '+@TblTable
  
    BEGIN TRANSACTION

    EXECUTE sp_rename @Tbl, @TblTable
    EXECUTE(@SQL)

    COMMIT TRANSACTION

    DELETE FROM @Objects WHERE Name = @Tbl
  END

  -- ReferenceSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceSearchParam_Table') AND name = 'IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId')
    CREATE UNIQUE INDEX IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId ON dbo.ReferenceSearchParam_Table 
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceSearchParam_Table') AND name = 'IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion')
    DROP INDEX IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion ON dbo.ReferenceSearchParam_Table

  -- DateTimeSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax')
	  CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
	  ON dbo.DateTimeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime')
    DROP INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime ON dbo.DateTimeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax')
	  CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsLongerThanADay_IsMin_IsMax
	  ON dbo.DateTimeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime')
    DROP INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime ON dbo.DateTimeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1')
	  CREATE INDEX IX_SearchParamId_StartDateTime_EndDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
	  ON dbo.DateTimeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long')
    DROP INDEX IX_DateTimeSearchParam_SearchParamId_StartDateTime_EndDateTime_Long ON dbo.DateTimeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1')
	  CREATE INDEX IX_SearchParamId_EndDateTime_StartDateTime_INCLUDE_IsMin_IsMax_WHERE_IsLongerThanADay_1
	  ON dbo.DateTimeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('DateTimeSearchParam_Table') AND name = 'IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long')
    DROP INDEX IX_DateTimeSearchParam_SearchParamId_EndDateTime_StartDateTime_Long ON dbo.DateTimeSearchParam_Table

  -- NumberSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam_Table') AND name = 'IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL')
	  CREATE INDEX IX_SearchParamId_SingleValue_WHERE_SingleValue_NOT_NULL
	  ON dbo.NumberSearchParam_Table
	  (
		  SearchParamId,
		  SingleValue
	  )
	  WHERE SingleValue IS NOT NULL
      WITH (ONLINE = ON)
	  ON PartitionScheme_ResourceTypeId(ResourceTypeId)

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam_Table') AND name = 'IX_NumberSearchParam_SearchParamId_SingleValue')
    DROP INDEX IX_NumberSearchParam_SearchParamId_SingleValue ON dbo.NumberSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam_Table') AND name = 'IX_SearchParamId_LowValue_HighValue')
	  CREATE INDEX IX_SearchParamId_LowValue_HighValue
	  ON dbo.NumberSearchParam_Table
	  (
		  SearchParamId,
		  LowValue,
		  HighValue
	  )
	  WITH (ONLINE = ON)
	  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam_Table') AND name = 'IX_NumberSearchParam_SearchParamId_LowValue_HighValue')
    DROP INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue ON dbo.NumberSearchParam_Table


  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam_Table') AND name = 'IX_SearchParamId_HighValue_LowValue')
	  CREATE INDEX IX_SearchParamId_HighValue_LowValue
	  ON dbo.NumberSearchParam_Table
	  (
		  SearchParamId,
		  HighValue,
		  LowValue
	  )
	  WITH (ONLINE = ON)
	  ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('NumberSearchParam_Table') AND name = 'IX_NumberSearchParam_SearchParamId_HighValue_LowValue')
    DROP INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue ON dbo.NumberSearchParam_Table


  --QuantitySearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam_Table') AND name = 'IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL')
      CREATE INDEX IX_SearchParamId_QuantityCodeId_SingleValue_INCLUDE_SystemId_WHERE_SingleValue_NOT_NULL
      ON dbo.QuantitySearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam_Table') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue')
    DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue ON dbo.QuantitySearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam_Table') AND name = 'IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId')
      CREATE INDEX IX_SearchParamId_QuantityCodeId_LowValue_HighValue_INCLUDE_SystemId
      ON dbo.QuantitySearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam_Table') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue')
    DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue ON dbo.QuantitySearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam_Table') AND name = 'IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId')
      CREATE INDEX IX_SearchParamId_QuantityCodeId_HighValue_LowValue_INCLUDE_SystemId
      ON dbo.QuantitySearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('QuantitySearchParam_Table') AND name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue')
    DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue ON dbo.QuantitySearchParam_Table

  --ReferenceTokenCompositeSearchParam

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceTokenCompositeSearchParam_Table') AND name = 'IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2')
      CREATE INDEX IX_SearchParamId_ReferenceResourceId1_Code2_INCLUDE_ReferenceResourceTypeId1_BaseUri1_SystemId2
      ON dbo.ReferenceTokenCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('ReferenceTokenCompositeSearchParam_Table') AND name = 'IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2')
    DROP INDEX IX_ReferenceTokenCompositeSearchParam_ReferenceResourceId1_Code2 ON dbo.ReferenceTokenCompositeSearchParam_Table

  --StringSearchParam

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam_Table') AND name = 'IX_StringSearchParam_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax')
      CREATE INDEX IX_StringSearchParam_SearchParamId_Text_INCLUDE_TextOverflow_IsMin_IsMax
      ON dbo.StringSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam_Table') AND name = 'IX_StringSearchParam_SearchParamId_Text')
    DROP INDEX IX_StringSearchParam_SearchParamId_Text ON dbo.StringSearchParam_Table


  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam_Table') AND name = 'IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL')
      CREATE INDEX IX_SearchParamId_Text_INCLUDE_IsMin_IsMax_WHERE_TextOverflow_NOT_NULL
      ON dbo.StringSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('StringSearchParam_Table') AND name = 'IX_StringSearchParam_SearchParamId_TextWithOverflow')
    DROP INDEX IX_StringSearchParam_SearchParamId_TextWithOverflow ON dbo.StringSearchParam_Table

  --TokenDateTimeCompositeSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2')
      CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
      ON dbo.TokenDateTimeCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2')
    DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2 ON dbo.TokenDateTimeCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2')
      CREATE INDEX IX_SearchParamId_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_IsLongerThanADay2
      ON dbo.TokenDateTimeCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2')
    DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2 ON dbo.TokenDateTimeCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1')
      CREATE INDEX IX_SearchParamId_Code1_StartDateTime2_EndDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
      ON dbo.TokenDateTimeCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long')
    DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long ON dbo.TokenDateTimeCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1')
      CREATE INDEX IX_Code1_EndDateTime2_StartDateTime2_INCLUDE_SystemId1_WHERE_IsLongerThanADay2_1
      ON dbo.TokenDateTimeCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenDateTimeCompositeSearchParam_Table') AND name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long')
    DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long ON dbo.TokenDateTimeCompositeSearchParam_Table

  --TokenNumberNumberCompositeSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_SingleValue2_SingleValue3_INCLUDE_SystemId1_WHERE_HasRange_0')
      CREATE INDEX IX_SearchParamId_Code1_SingleValue2_SingleValue3_INCLUDE_SystemId1_WHERE_HasRange_0
      ON dbo.TokenNumberNumberCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam_Table') AND name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2')
    DROP INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2 ON dbo.TokenNumberNumberCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3_INCLUDE_SystemId1_WHERE_HasRange_1')
      CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3_INCLUDE_SystemId1_WHERE_HasRange_1
      ON dbo.TokenNumberNumberCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenNumberNumberCompositeSearchParam_Table') AND name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3')
    DROP INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3 ON dbo.TokenNumberNumberCompositeSearchParam_Table

  --TokenQuantityCompositeSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_SingleValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_SingleValue2_NOT_NULL')
      CREATE INDEX IX_SearchParamId_Code1_SingleValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_SingleValue2_NOT_NULL
      ON dbo.TokenQuantityCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam_Table') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2')
    DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2 ON dbo.TokenQuantityCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_LowValue2_HighValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL')
      CREATE INDEX IX_SearchParamId_Code1_LowValue2_HighValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
      ON dbo.TokenQuantityCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam_Table') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2')
    DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2 ON dbo.TokenQuantityCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_HighValue2_LowValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL')
      CREATE INDEX IX_SearchParamId_Code1_HighValue2_LowValue2_INCLUDE_QuantityCodeId2_SystemId1_SystemId2_WHERE_LowValue2_NOT_NULL
      ON dbo.TokenQuantityCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenQuantityCompositeSearchParam_Table') AND name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2')
    DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2 ON dbo.TokenQuantityCompositeSearchParam_Table

  --TokenStringCompositeSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_TextOverflow2')
      CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_TextOverflow2
      ON dbo.TokenStringCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam_Table') AND name = 'IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2')
    DROP INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2 ON dbo.TokenStringCompositeSearchParam_Table

  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_WHERE_TextOverflow2_NOT_NULL')
      CREATE INDEX IX_SearchParamId_Code1_Text2_INCLUDE_SystemId1_WHERE_TextOverflow2_NOT_NULL
      ON dbo.TokenStringCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenStringCompositeSearchParam_Table') AND name = 'IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow')
    DROP INDEX IX_TokenStringCompositeSearchParam_SearchParamId_Code1_Text2WithOverflow ON dbo.TokenStringCompositeSearchParam_Table

  --TokenTokenCompositeSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenTokenCompositeSearchParam_Table') AND name = 'IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2')
      CREATE INDEX IX_SearchParamId_Code1_Code2_INCLUDE_SystemId1_SystemId2
      ON dbo.TokenTokenCompositeSearchParam_Table
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenTokenCompositeSearchParam_Table') AND name = 'IX_TokenTokenCompositeSearchParam_Code1_Code2')
    DROP INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2 ON dbo.TokenTokenCompositeSearchParam_Table

  --UriSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('UriSearchParam_Table') AND name = 'IX_SearchParamId_Uri')
      CREATE INDEX IX_SearchParamId_Uri
      ON dbo.UriSearchParam_Table
      (
          SearchParamId,
          Uri
      )
      WITH (DATA_COMPRESSION = PAGE, ONLINE = ON)
      ON PartitionScheme_ResourceTypeId (ResourceTypeId)

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('UriSearchParam_Table') AND name = 'IX_UriSearchParam_SearchParamId_Uri')
    DROP INDEX IX_UriSearchParam_SearchParamId_Uri ON dbo.UriSearchParam_Table

  --TokenSearchParam
  IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParam_Table') AND name = 'IX_SearchParamId_Code_INCLUDE_SystemId')
      CREATE INDEX IX_SearchParamId_Code_INCLUDE_SystemId 
      ON dbo.TokenSearchParam_Table 
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

  IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('TokenSearchParam_Table') AND name = 'IX_TokenSeachParam_SearchParamId_Code_SystemId')
    DROP INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId ON dbo.TokenSearchParam_Table
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  THROW
END CATCH
GO

-- Hotfix bounday: Below should be applied after deployment ------------------------------------------------------------------------
BEGIN TRY
  DECLARE @Objects TABLE (Name varchar(100) PRIMARY KEY)
  INSERT INTO @Objects SELECT name FROM sys.objects WHERE type = 'v' AND name LIKE '%SearchParam'

  DECLARE @Tbl varchar(100)
         ,@TblTable varchar(100)

  WHILE EXISTS (SELECT * FROM @Objects)
  BEGIN
    SET @Tbl = (SELECT TOP 1 Name FROM @Objects)
    SET @TblTable = @Tbl + '_Table'

    BEGIN TRANSACTION

    EXECUTE('DROP VIEW '+@Tbl)
    EXECUTE sp_rename @TblTable, @Tbl

    COMMIT TRANSACTION

    DELETE FROM @Objects WHERE Name = @Tbl
  END
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION;
  THROW
END CATCH
GO

-- Delete search parameter data for historical resources --
set nocount on

INSERT INTO dbo.Parameters (Id, Char) SELECT 'SearchParamsDeleteHistory', 'LogEvent'
EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='Start'

DECLARE @Types TABLE (ResourceTypeId smallint PRIMARY KEY, Name varchar(100))
DECLARE @Tables TABLE (Name varchar(100))

DECLARE @ResourceTypeId smallint
       ,@Process varchar(100) = 'SearchParamsDeleteHistory'
       ,@Id varchar(100) = 'SearchParamsDeleteHistory.LastProcessed.TypeId.SurrogateId.SearchParamId'
       ,@SurrogateId bigint
       ,@Rows int
       ,@MaxSurrogateId bigint = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000
       ,@CurrentMaxSurrogateId bigint
       ,@LastProcessed varchar(100)
       ,@st datetime
       ,@Table varchar(100)
       ,@SQL nvarchar(max)
       ,@ClusteredIndexRows bigint
       ,@FilteredIndexRows bigint

-- restart
--DELETE FROM Parameters WHERE Id = @Id

IF object_id('tempdb..#ids') IS NULL
  SELECT ResourceSurrogateId INTO #Ids FROM dbo.ReferenceSearchParam WHERE 1 = 2

BEGIN TRY
  INSERT INTO dbo.Parameters (Id, Char) SELECT @Id, '0.0.0' WHERE NOT EXISTS (SELECT * FROM dbo.Parameters WHERE Id = @Id)

  SET @LastProcessed = (SELECT Char FROM dbo.Parameters WHERE Id = @Id)

  -- Ignore previously updated quantity and number tables
  INSERT INTO @Tables SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam' AND name <> 'SearchParam' AND name NOT LIKE '%Number%' AND name NOT LIKE '%Quantity%' ORDER BY name
  
  SET @Table = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 1) --substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1)
  DELETE FROM @Tables WHERE Name < @Table
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Tables',@Action='Delete',@Rows=@@rowcount

  SET @ResourceTypeId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 2) --substring(@LastProcessed, 1, charindex('.', @LastProcessed) - 1)
  SET @SurrogateId = (SELECT value FROM string_split(@LastProcessed, '.', 1) WHERE ordinal = 3) --substring(@LastProcessed, charindex('.', @LastProcessed) + 1, 255)

  WHILE EXISTS (SELECT * FROM @Tables) -- Processing in ASC order
  BEGIN
    SET @Table = (SELECT TOP 1 Name FROM @Tables ORDER BY Name)

    INSERT INTO @Types EXECUTE dbo.GetUsedResourceTypes
    EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Types',@Action='Insert',@Rows=@@rowcount

    DELETE FROM @Types WHERE ResourceTypeId < @ResourceTypeId
    EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Target='@Types',@Action='Delete',@Rows=@@rowcount

    WHILE EXISTS (SELECT * FROM @Types) -- Processing in ASC order
    BEGIN
      SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @Types ORDER BY ResourceTypeId)
      SET @LastProcessed = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@SurrogateId)

      SET @FilteredIndexRows = (SELECT sum(row_count) 
                                  FROM sys.dm_db_partition_stats 
                                  WHERE object_id = object_id(@Table) 
                                    AND index_id > 1 
                                    AND index_id = (SELECT TOP 1 index_id FROM sys.indexes WHERE object_id = object_id(@Table) AND index_id > 1 AND replace(replace(replace(replace(filter_definition,'[',''),']',''),'(',''),')','') = 'IsHistory=0')
                                    AND partition_number = $PARTITION.PartitionFunction_ResourceTypeId(@ResourceTypeId)
                                  GROUP BY 
                                       index_id
                               )
      EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Count',@Rows=@FilteredIndexRows,@Text='Filtered Index'
      SET @ClusteredIndexRows = (SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id(@Table) AND index_id = 1 AND partition_number = $PARTITION.PartitionFunction_ResourceTypeId(@ResourceTypeId))
      EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Count',@Rows=@ClusteredIndexRows,@Text='Clustered'

      IF @ClusteredIndexRows = @FilteredIndexRows
      BEGIN
        EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Count',@Rows=@ClusteredIndexRows,@Text='Clustered=Filteted'
        UPDATE dbo.Parameters SET Char = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@MaxSurrogateId) WHERE Id = @Id
      END
      ELSE
      BEGIN
        SET @CurrentMaxSurrogateId = 0
        WHILE @CurrentMaxSurrogateId IS NOT NULL
        BEGIN -- @SurrogateId
          SET @LastProcessed = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@SurrogateId)
        
          TRUNCATE TABLE #Ids
          SET @st = getUTCdate()
          SET @SQL = N'
INSERT INTO #Ids
  SELECT TOP 100000 
         ResourceSurrogateId
    FROM dbo.'+@Table+' WITH (INDEX = 1)
    WHERE ResourceTypeId = @ResourceTypeId 
      AND ResourceSurrogateId >= @SurrogateId
      AND IsHistory = 1
    ORDER BY 
         ResourceSurrogateId, SearchParamId
            '
          EXECUTE sp_executeSQL @SQL, N'@ResourceTypeId smallint, @SurrogateId bigint', @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId
          EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Select',@Rows=@@rowcount,@Start=@st

          SET @CurrentMaxSurrogateId = NULL
          SELECT @CurrentMaxSurrogateId = max(ResourceSurrogateId) FROM #Ids

          IF @CurrentMaxSurrogateId IS NOT NULL
          BEGIN
            SET @LastProcessed = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,@CurrentMaxSurrogateId)

            SET @st = getUTCdate()
            SET @SQL = N'
DELETE FROM dbo.'+@Table+'
  WHERE ResourceTypeId = @ResourceTypeId 
  AND ResourceSurrogateId >= @SurrogateId AND ResourceSurrogateId <= @CurrentMaxSurrogateId
  AND IsHistory = 1
            '
            EXECUTE sp_executeSQL @SQL, N'@ResourceTypeId smallint, @SurrogateId bigint, @CurrentMaxSurrogateId bigint', @ResourceTypeId = @ResourceTypeId, @SurrogateId = @SurrogateId, @CurrentMaxSurrogateId = @CurrentMaxSurrogateId
            EXECUTE dbo.LogEvent @Process=@Process,@Status='Run',@Mode=@LastProcessed,@Target=@Table,@Action='Delete',@Rows=@@rowcount,@Start=@st

            SET @SurrogateId = @CurrentMaxSurrogateId
          END

          UPDATE dbo.Parameters SET Char = @Table+'.'+convert(varchar,@ResourceTypeId)+'.'+convert(varchar,isnull(@CurrentMaxSurrogateId,@MaxSurrogateId)) WHERE Id = @Id
        END -- @SurrogateId
      END -- skip if rowcounts match

      DELETE FROM @Types WHERE ResourceTypeId = @ResourceTypeId

      SET @SurrogateId = 0
    END -- @Types

    DELETE FROM @Tables WHERE Name = @Table

    SET @ResourceTypeId = 0
  END -- @Tables
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@Process,@Status='Error';
  THROW
END CATCH

EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='End'

--SELECT TOP 10000 * FROM EventLog WHERE EventDate > dateadd(hour,-1,getUTCdate()) AND Process='SearchParamsDeleteHistory' ORDER BY EventDate DESC, EventId DESC
--SELECT * FROM Parameters WHERE Id = 'SearchParamsDeleteHistory.LastProcessed.TypeId.SurrogateId.SearchParamId'
--SELECT ','+name FROM sys.columns WHERE object_id = object_id('TokenSearchParam') ORDER BY column_id
--INSERT INTO ReferenceSearchParam
--     (   
--         ResourceTypeId
--        ,ResourceSurrogateId
--        ,SearchParamId
--        ,BaseUri
--        ,ReferenceResourceTypeId
--        ,ReferenceResourceId
--        ,ReferenceResourceVersion
--        ,IsHistory 
--    )
--  SELECT TOP 100000 
--         ResourceTypeId
--        ,ResourceSurrogateId - 1e10
--        ,SearchParamId
--        ,BaseUri
--        ,ReferenceResourceTypeId
--        ,ReferenceResourceId
--        ,ReferenceResourceVersion
--        ,IsHistory = 1
--    FROM ReferenceSearchParam 
--    WHERE ResourceTypeId = 96 
--INSERT INTO TokenSearchParam
--     (   
--         ResourceTypeId
--        ,ResourceSurrogateId
--        ,SearchParamId
--        ,SystemId
--        ,Code
--        ,IsHistory
--        ,CodeOverflow
--    )
--  SELECT TOP 100000 
--         ResourceTypeId
--        ,ResourceSurrogateId - 1e10
--        ,SearchParamId
--        ,SystemId
--        ,Code
--        ,IsHistory = 1
--        ,CodeOverflow
--    FROM TokenSearchParam 
--    WHERE ResourceTypeId = 96 
