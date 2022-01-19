/*************************************************************
    This migration adds primary keys to the existing tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 26.';
GO

/******************************************************************
  Table - dbo.TokenText table
*******************************************************************/
-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenText'
GO

WITH cte AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory
	) row_num
	FROM dbo.TokenText
)
DELETE FROM cte WHERE row_num > 1
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenText' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenText'
	ALTER TABLE dbo.TokenText 
	ADD CONSTRAINT PK_TokenText PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
GO

/******************************************************************
  Table - dbo.DateTimeSearchParam table
*******************************************************************/
-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_DateTimeSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_DateTimeSearchParam'
	ALTER TABLE dbo.DateTimeSearchParam 
	ADD CONSTRAINT PK_DateTimeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END
GO

/************************************************************************
 Insert an entry with id 0 to dbo.QuantityCode table
************************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Insert an entry with id 0 to dbo.QuantityCode table'
IF NOT EXISTS (SELECT 1 FROM dbo.QuantityCode WHERE QuantityCodeId = 0 AND value = '')
BEGIN
    SET IDENTITY_INSERT dbo.QuantityCode ON;

    Insert INTO dbo.QuantityCode (QuantityCodeId, Value)
    Values (0, '')

    SET IDENTITY_INSERT dbo.QuantityCode OFF;
END;
GO

/***************************************************************
 Insert an entry with id 0 to dbo.System table
****************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Insert an entry with id 0 to dbo.System table'
IF NOT EXISTS (SELECT 1 FROM dbo.System WHERE SystemId = 0 AND value = '')
BEGIN
    SET IDENTITY_INSERT dbo.System ON;

    Insert INTO dbo.System (SystemId, Value)
    Values (0, '')

    SET IDENTITY_INSERT dbo.System OFF;
END;
GO

/******************************************************************
  Table - dbo.QuantitySearchParam table
*******************************************************************/
-- Backfill table dbo.QuantitySearchParam with non-null QuantityCodeId value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill table dbo.QuantitySearchParam'
UPDATE dbo.QuantitySearchParam
SET QuantityCodeId = 0 
WHERE QuantityCodeId IS NULL;
GO

-- Backfill table dbo.QuantitySearchParam with non-null SystemId value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill table dbo.QuantitySearchParam'
UPDATE dbo.QuantitySearchParam
SET SystemId = 0 
WHERE SystemId IS NULL;
GO

-- Drop exisiting indexes
-- Update QuantityCodeId and SystemId column as NOT NULL
IF ((SELECT COLUMNPROPERTY(OBJECT_ID('dbo.QuantitySearchParam', 'U'), 'QuantityCodeId', 'AllowsNull')) = 1
    OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.QuantitySearchParam', 'U'), 'SystemId', 'AllowsNull')) = 1)
BEGIN
    -- Dropping indexes that uses QuantityCodeId and SystemId column
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'PK_QuantitySearchParam')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping PK_QuantitySearchParam'
        ALTER TABLE dbo.QuantitySearchParam
		DROP CONSTRAINT PK_QuantitySearchParam
	END;

	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue'
		DROP INDEX IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue
		ON dbo.QuantitySearchParam
	END;

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

    -- Updating QuantityCodeId column as NOT NULL
    EXEC dbo.LogSchemaMigrationProgress 'Updating QuantitySearchParam.QuantityCodeId as NOT NULL'
	ALTER TABLE dbo.QuantitySearchParam
	ALTER COLUMN QuantityCodeId INT NOT NULL;

    -- Updating SystemId column as NOT NULL
    EXEC dbo.LogSchemaMigrationProgress 'Updating QuantitySearchParam.SystemId as NOT NULL'
	ALTER TABLE dbo.QuantitySearchParam
	ALTER COLUMN SystemId INT NOT NULL;
END;
GO

-- Adding default constraint to QuantityCodeId column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_QuantitySearchParam_QuantityCodeId' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding default constraint to QuantityCodeId column'
    ALTER TABLE dbo.QuantitySearchParam
    ADD CONSTRAINT DF_QuantitySearchParam_QuantityCodeId
    DEFAULT 0 FOR QuantityCodeId
END;
GO

-- Adding default constraint to SystemId column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_QuantitySearchParam_SystemId' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding default constraint to SystemId column'
    ALTER TABLE dbo.QuantitySearchParam
    ADD CONSTRAINT DF_QuantitySearchParam_SystemId
    DEFAULT 0 FOR SystemId
END;
GO

-- Recreate dropped indexes consisting QuantityCodeId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_SingleValue')
BEGIN
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
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_LowValue_HighValue')
BEGIN
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
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_QuantitySearchParam_SearchParamId_QuantityCodeId_HighValue_LowValue')
BEGIN
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
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_QuantitySearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_QuantitySearchParam'
	ALTER TABLE dbo.QuantitySearchParam 
	ADD CONSTRAINT PK_QuantitySearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, QuantityCodeId, HighValue, LowValue)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END
GO
