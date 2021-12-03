EXEC dbo.LogSchemaMigrationProgress 'Beginning migration to version 23.';
GO

/*************************************************************
 Insert singleValue values into the low and high values for
 dbo.QuantitySearchParam table
**************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Populating LowValue and HighValue in QuantitySearchParam if null'
UPDATE dbo.QuantitySearchParam
SET LowValue = SingleValue, 
    HighValue = SingleValue
WHERE LowValue IS NULL
    AND HighValue IS NULL
    AND SingleValue IS NOT NULL;
GO

/*************************************************************
Table - dbo.QuantitySearchParam
**************************************************************/
-- Update LowValue and HighValue columns as NOT NULL
IF (((SELECT COLUMNPROPERTY(OBJECT_ID('dbo.QuantitySearchParam', 'U'), 'LowValue', 'AllowsNull')) = 1)
    OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.QuantitySearchParam', 'U'), 'HighValue', 'AllowsNull')) = 1)
BEGIN
    -- Drop indexes that uses LowValue and HighValue columns
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue'
		DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue
		ON dbo.QuantitySearchParam
	END;

	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue'
		DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue
		ON dbo.QuantitySearchParam
	END;

	-- Update datatype and non-nullable LowValue and HighValue columns
	EXEC dbo.LogSchemaMigrationProgress 'Updating LowValue as NOT NULL'
	ALTER TABLE dbo.QuantitySearchParam
	ALTER COLUMN LowValue decimal(18,6) NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating HighValue as NOT NULL'
	ALTER TABLE dbo.QuantitySearchParam
	ALTER COLUMN HighValue decimal(18,6) NOT NULL;	
END;
GO

-- Recreate dropped indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue'

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
	WHERE IsHistory = 0
    WITH (ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue'
	
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
    WHERE IsHistory = 0
    WITH (ONLINE=ON) 
    ON PartitionScheme_ResourceTypeId(ResourceTypeId);
END;
GO

IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_QuantitySearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_QuantitySearchParam'
	ALTER TABLE dbo.QuantitySearchParam 
	ADD CONSTRAINT PK_QuantitySearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, HighValue, LowValue, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

/*************************************************************
 Insert singleValue values into the low and high values for
 dbo.NumberSearchParam table
**************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Populating LowValue and HighValue in NumberSearchParam if null'
UPDATE dbo.NumberSearchParam
SET LowValue = SingleValue, 
    HighValue = SingleValue
WHERE LowValue IS NULL
    AND HighValue IS NULL
    AND SingleValue IS NOT NULL;
GO

/*************************************************************
Table - dbo.NumberSearchParam
**************************************************************/
-- Update LowValue and HighValue column as NOT NULL
IF (((SELECT COLUMNPROPERTY(OBJECT_ID('dbo.NumberSearchParam', 'U'), 'LowValue', 'AllowsNull')) = 1)
    OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.NumberSearchParam', 'U'), 'HighValue', 'AllowsNull')) = 1)
BEGIN
 -- Drop indexes that uses lowValue and HighValue column
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NumberSearchParam_SearchParamId_LowValue_HighValue')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_NumberSearchParam_SearchParamId_LowValue_HighValue'
		DROP INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
		ON dbo.NumberSearchParam
	END;

	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NumberSearchParam_SearchParamId_HighValue_LowValue')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_NumberSearchParam_SearchParamId_HighValue_LowValue'
		DROP INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
		ON dbo.NumberSearchParam
	END;

	-- Update datatype and non-nullable LowValue and HighValue columns
	EXEC dbo.LogSchemaMigrationProgress 'Updating NumberSearchParam LowValue as NOT NULL'
	ALTER TABLE dbo.NumberSearchParam
	ALTER COLUMN LowValue decimal(18,6) NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating NumberSearchParam HighValue as NOT NULL'
	ALTER TABLE dbo.NumberSearchParam
	ALTER COLUMN HighValue decimal(18,6) NOT NULL;
END;
GO

-- Recreate dropped indexes
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NumberSearchParam_SearchParamId_HighValue_LowValue')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_NumberSearchParam_SearchParamId_HighValue_LowValue'
    CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_HighValue_LowValue
    ON dbo.NumberSearchParam
    (
	    ResourceTypeId,
	    SearchParamId,
	    HighValue,
	    LowValue,
	    ResourceSurrogateId
    )
    WHERE IsHistory = 0
    WITH (ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_NumberSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_NumberSearchParam'
	ALTER TABLE dbo.NumberSearchParam 
	ADD CONSTRAINT PK_NumberSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, LowValue, HighValue, ResourceSurrogateId)
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_NumberSearchParam_SearchParamId_LowValue_HighValue')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_NumberSearchParam_SearchParamId_LowValue_HighValue'
    CREATE NONCLUSTERED INDEX IX_NumberSearchParam_SearchParamId_LowValue_HighValue
    ON dbo.NumberSearchParam
    (
        ResourceTypeId,
        SearchParamId,
        LowValue,
        HighValue,
        ResourceSurrogateId
    )
    WHERE IsHistory = 0
    WITH (ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO
