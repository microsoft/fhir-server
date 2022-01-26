/*************************************************************
    This migration adds primary keys to the existing tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 26.';
GO

/******************************************************************
  Table - dbo.TokenText table
*******************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenText';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
	) row_num
	FROM dbo.TokenText
)
DELETE FROM CTE WHERE row_num > 1;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenText' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenText';
	ALTER TABLE dbo.TokenText 
	ADD CONSTRAINT PK_TokenText PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Text)
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

/******************************************************************
  Table - dbo.DateTimeSearchParam table
*******************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.DateTimeSearchParam';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId
	) row_num
	FROM dbo.DateTimeSearchParam
)
DELETE FROM CTE WHERE row_num > 1;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_DateTimeSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_DateTimeSearchParam';
	ALTER TABLE dbo.DateTimeSearchParam 
	ADD CONSTRAINT PK_DateTimeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END;
GO

/************************************************************************
 Insert an entry with id 0 to dbo.QuantityCode table
************************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Insert an entry with id 0 to dbo.QuantityCode table';
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
EXEC dbo.LogSchemaMigrationProgress 'Insert an entry with id 0 to dbo.System table';
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

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.QuantitySearchParam';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue
	) row_num
	FROM dbo.QuantitySearchParam
)
DELETE FROM CTE WHERE row_num > 1;
GO

-- Backfill table dbo.QuantitySearchParam with non-null QuantityCodeId value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column QuantityCodeId into the table dbo.QuantitySearchParam';
UPDATE dbo.QuantitySearchParam
SET QuantityCodeId = 0 
WHERE QuantityCodeId IS NULL;
GO

-- Backfill table dbo.QuantitySearchParam with non-null SystemId value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column SystemId into the table dbo.QuantitySearchParam';
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
END;
GO

/******************************************************************
  Table - dbo.TokenSearchParam table
*******************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenSearchParam';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory
	) ROW_NUM
	FROM dbo.TokenSearchParam
)
DELETE FROM CTE WHERE ROW_NUM > 1;
GO

-- Backfill table dbo.TokenSearchParam with non-null system value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column SystemId into the table dbo.TokenSearchParam';
GO
UPDATE dbo.TokenSearchParam
SET SystemId = 0 
WHERE SystemId IS NULL;
GO

-- Update SystemId column as NOT NULL
IF (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenSearchParam', 'U'), 'SystemId', 'AllowsNull')) = 1
BEGIN
    -- Drop indexes that uses systemId1 column
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenSeachParam_SearchParamId_Code_SystemId')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenSeachParam_SearchParamId_Code_SystemId';
		DROP INDEX IX_TokenSeachParam_SearchParamId_Code_SystemId
		ON dbo.TokenSearchParam
	END;

    -- Update SystemId column as non-nullable 
    EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId as NOT NULL';
	ALTER TABLE dbo.TokenSearchParam
	ALTER COLUMN SystemId INT NOT NULL;
END;
GO

-- Adding default constraint to SystemId column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenSearchParam_SystemId' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding default constraint to SystemId column'
    ALTER TABLE dbo.TokenSearchParam
    ADD CONSTRAINT DF_TokenSearchParam_SystemId
    DEFAULT 0 FOR SystemId;
END;
GO

-- Recreate dropped indexes consisting systemId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenSeachParam_SearchParamId_Code_SystemId')
BEGIN
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenSearchParam';
	ALTER TABLE dbo.TokenSearchParam 
	ADD CONSTRAINT PK_TokenSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END;
GO

/****************************************************************************************
  Table - dbo.TokenDateTimeCompositeSearchParam
*****************************************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenDateTimeCompositeSearchParam';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory
	) ROW_NUM
	FROM dbo.TokenDateTimeCompositeSearchParam
)
DELETE FROM CTE WHERE ROW_NUM > 1;
GO

-- Backfill table dbo.TokenDateTimeCompositeSearchParam with non-null system value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column SystemId1 into the table dbo.TokenDateTimeCompositeSearchParam';

UPDATE dbo.TokenDateTimeCompositeSearchParam
SET SystemId1 = 0 
WHERE SystemId1 IS NULL;
GO

-- Update SystemId1 column as NOT NULL and Add default constraint
IF (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenDateTimeCompositeSearchParam', 'U'), 'SystemId1', 'AllowsNull')) = 1
BEGIN
    -- Drop indexes that uses systemId1 column
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2';
		DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2
		ON dbo.TokenDateTimeCompositeSearchParam
	END;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2';
		DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2
		ON dbo.TokenDateTimeCompositeSearchParam
	END;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long';
		DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long
		ON dbo.TokenDateTimeCompositeSearchParam
	END;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long';
		DROP INDEX IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long
		ON dbo.TokenDateTimeCompositeSearchParam
	END;

    -- Update SystemId1 column as non-nullable 
    EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId1 as NOT NULL';
	ALTER TABLE dbo.TokenDateTimeCompositeSearchParam
	ALTER COLUMN SystemId1 INT NOT NULL;
END;
GO

-- Adding default constraint to SystemId1 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenDateTimeCompositeSearchParam_SystemId1' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to SystemId1 column';
    ALTER TABLE dbo.TokenDateTimeCompositeSearchParam
    ADD CONSTRAINT DF_TokenDateTimeCompositeSearchParam_SystemId1
    DEFAULT 0 FOR SystemId1;
END;
GO

-- Recreate dropped indexes consisting systemId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenDateTimeCompositeSearchParam_Code1_StartDateTime2_EndDateTime2_Long';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenDateTimeCompositeSearchParam_Code1_EndDateTime2_StartDateTime2_Long';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenDateTimeCompositeSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenDateTimeCompositeSearchParam';
	ALTER TABLE dbo.TokenDateTimeCompositeSearchParam 
	ADD CONSTRAINT PK_TokenDateTimeCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END;
GO

/*************************************************************
 Table - dbo.TokenNumberNumberCompositeSearchParam table
**************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenNumberNumberCompositeSearchParam';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory
	) ROW_NUM
	FROM dbo.TokenNumberNumberCompositeSearchParam
)
DELETE FROM CTE WHERE ROW_NUM > 1;
GO

-- Backfill table dbo.TokenNumberNumberCompositeSearchParam with non-null system value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column SystemId1 into the table dbo.TokenNumberNumberCompositeSearchParam';
UPDATE dbo.TokenNumberNumberCompositeSearchParam
SET SystemId1 = 0 
WHERE SystemId1 IS NULL;
GO

-- Insert singleValue values into the low and high values for dbo.TokenNumberNumberCompositeSearchParam table
EXEC dbo.LogSchemaMigrationProgress 'Populating LowValue and HighValue in TokenNumberNumberCompositeSearchParam if null';

UPDATE dbo.TokenNumberNumberCompositeSearchParam
SET LowValue2 = SingleValue2, 
    HighValue2 = SingleValue2
WHERE LowValue2 IS NULL
    AND HighValue2 IS NULL
    AND SingleValue2 IS NOT NULL;

UPDATE dbo.TokenNumberNumberCompositeSearchParam
SET LowValue3 = SingleValue3, 
    HighValue3 = SingleValue3
WHERE LowValue3 IS NULL
    AND HighValue3 IS NULL
    AND SingleValue3 IS NOT NULL;
GO

-- Update LowValue, HighValue and SystemId1 columns as NOT NULL and Add default constraint to systemId1
IF ((SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam', 'U'), 'LowValue2', 'AllowsNull')) = 1
    OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam', 'U'), 'HighValue2', 'AllowsNull')) = 1
       OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam', 'U'), 'LowValue3', 'AllowsNull')) = 1
          OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam', 'U'), 'HighValue3', 'AllowsNull')) = 1
            OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam', 'U'), 'SystemId1', 'AllowsNull')) = 1)
BEGIN
    -- Drop indexes that uses LowValue and HighValue columns
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3';
		DROP INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3
		ON dbo.TokenNumberNumberCompositeSearchParam
	END;

    -- Drop indexes that uses systemId1 column
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2';
		DROP INDEX IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2
		ON dbo.TokenNumberNumberCompositeSearchParam
	END;

	-- Update LowValue and HighValue columns as non-nullable 
	EXEC dbo.LogSchemaMigrationProgress 'Updating LowValue2 as NOT NULL';
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ALTER COLUMN LowValue2 decimal(18,6) NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating HighValue2 as NOT NULL';
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ALTER COLUMN HighValue2 decimal(18,6) NOT NULL;

    EXEC dbo.LogSchemaMigrationProgress 'Updating LowValue3 as NOT NULL';
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ALTER COLUMN LowValue3 decimal(18,6) NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating HighValue3 as NOT NULL';
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ALTER COLUMN HighValue3 decimal(18,6) NOT NULL;

    -- Update SystemId1 column as non-nullable 
    EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId1 as NOT NULL';
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ALTER COLUMN SystemId1 INT NOT NULL;
END;
GO

-- Adding default constraint to SystemId1 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenNumberNumberCompositeSearchParam_SystemId1' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to SystemId1 column';
    ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
    ADD CONSTRAINT DF_TokenNumberNumberCompositeSearchParam_SystemId1
    DEFAULT 0 FOR SystemId1;
END;
GO

-- Recreate dropped indexes consisting low and high values
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_LowValue2_HighValue2_LowValue3_HighValue3';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

-- Recreate dropped indexes consisting systemId1
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenNumberNumberCompositeSearchParam_SearchParamId_Code1_Text2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenNumberNumberCompositeSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenNumberNumberCompositeSearchParam';
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam 
	ADD CONSTRAINT PK_TokenNumberNumberCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, LowValue2, HighValue2, LowValue3, HighValue3)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END;
GO

/*************************************************************
 Table - dbo.TokenQuantityCompositeSearchParam
**************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenQuantityCompositeSearchParam';
GO
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, QuantityCodeId2, SingleValue2, LowValue2, HighValue2, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, QuantityCodeId2, SingleValue2, LowValue2, HighValue2, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, QuantityCodeId2, SingleValue2, LowValue2, HighValue2, IsHistory
	) ROW_NUM
	FROM dbo.TokenQuantityCompositeSearchParam
)
DELETE FROM CTE WHERE ROW_NUM > 1;
GO

--  Backfill table dbo.TokenQuantityCompositeSearchParam with non-null system and quantitycode value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column SystemId1, SystemId2 and QuantityCodeId2 into the table dbo.QuantitySearchParam';

UPDATE dbo.TokenQuantityCompositeSearchParam
SET SystemId1 = 0 
WHERE SystemId1 IS NULL;

UPDATE dbo.TokenQuantityCompositeSearchParam
SET SystemId2 = 0 
WHERE SystemId2 IS NULL;

UPDATE dbo.TokenQuantityCompositeSearchParam
SET QuantityCodeId2 = 0 
WHERE QuantityCodeId2 IS NULL;
GO

--  Insert singleValue values into the low and high values for dbo.TokenQuantityCompositeSearchParam table
EXEC dbo.LogSchemaMigrationProgress 'Populating LowValue and HighValue in TokenQuantityCompositeSearchParam if null';
UPDATE dbo.TokenQuantityCompositeSearchParam
SET LowValue2 = SingleValue2, 
    HighValue2 = SingleValue2
WHERE LowValue2 IS NULL
    AND HighValue2 IS NULL
    AND SingleValue2 IS NOT NULL;
GO

-- Update LowValue, HighValue, QuantityCodeId2, SystemId1 and SystemId2 columns as NOT NULL and Add default constraint to systemId1
IF ((SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenQuantityCompositeSearchParam', 'U'), 'LowValue2', 'AllowsNull')) = 1
    OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenQuantityCompositeSearchParam', 'U'), 'HighValue2', 'AllowsNull')) = 1
       OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenQuantityCompositeSearchParam', 'U'), 'QuantityCodeId2', 'AllowsNull')) = 1
          OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenQuantityCompositeSearchParam', 'U'), 'SystemId1', 'AllowsNull')) = 1
             OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenQuantityCompositeSearchParam', 'U'), 'SystemId2', 'AllowsNull')) = 1)
BEGIN
    -- Drop indexes that uses lowValue, highValue, QuantityCodeId2, SystemId1 and SystemId2 values
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2';
		DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2
		ON dbo.TokenQuantityCompositeSearchParam
	END;

	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2';
		DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2
		ON dbo.TokenQuantityCompositeSearchParam
	END;

	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2';
		DROP INDEX IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2
		ON dbo.TokenQuantityCompositeSearchParam
	END;

	-- Update LowValue and HighValue columns as non-nullable 
	EXEC dbo.LogSchemaMigrationProgress 'Updating LowValue2 as NOT NULL';
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ALTER COLUMN LowValue2 decimal(18,6) NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating HighValue2 as NOT NULL';
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ALTER COLUMN HighValue2 decimal(18,6) NOT NULL;

    -- Update SystemId1 and SystemId2 column as non-nullable 
    EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId1 as NOT NULL';
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ALTER COLUMN SystemId1 INT NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId2 as NOT NULL';
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ALTER COLUMN SystemId2 INT NOT NULL;

    -- Update QuantityCodeId2 column as non-nullable 
    EXEC dbo.LogSchemaMigrationProgress 'Updating QuantityCodeId2 as NOT NULL';
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ALTER COLUMN QuantityCodeId2 int NOT NULL;
END;
GO

-- Adding default constraint to SystemId1 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenQuantityCompositeSearchParam_SystemId1' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to SystemId1 column';
    ALTER TABLE dbo.TokenQuantityCompositeSearchParam
    ADD CONSTRAINT DF_TokenQuantityCompositeSearchParam_SystemId1
    DEFAULT 0 FOR SystemId1;
END;
GO

-- Adding default constraint to SystemId2 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenQuantityCompositeSearchParam_SystemId2' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to SystemId2 column';
    ALTER TABLE dbo.TokenQuantityCompositeSearchParam
    ADD CONSTRAINT DF_TokenQuantityCompositeSearchParam_SystemId2
    DEFAULT 0 FOR SystemId2;
END;
GO

-- Adding default constraint to QuantityCodeId2 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenQuantityCompositeSearchParam_QuantityCodeId2' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to QuantityCodeId2 column';
    ALTER TABLE dbo.TokenQuantityCompositeSearchParam
    ADD CONSTRAINT DF_TokenQuantityCompositeSearchParam_QuantityCodeId2
    DEFAULT 0 FOR QuantityCodeId2;
END;
GO

-- Recreate dropped indexes consisting lowValue, highValue, QuantityCodeId2, SystemId1 and SystemId2 values
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_SingleValue2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_LowValue2_HighValue2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenQuantityCompositeSearchParam_SearchParamId_Code1_QuantityCodeId2_HighValue2_LowValue2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenQuantityCompositeSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenQuantityCompositeSearchParam';
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam 
	ADD CONSTRAINT PK_TokenQuantityCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, QuantityCodeId2, LowValue2, HighValue2)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId) 
END;

GO

/****************************************************************************************
 Table dbo.TokenTokenCompositeSearchParam
*****************************************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.TokenTokenCompositeSearchParam'
;WITH CTE AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory
	) ROW_NUM
	FROM dbo.TokenTokenCompositeSearchParam
)
DELETE FROM CTE WHERE ROW_NUM > 1;
GO

-- Backfill table dbo.TokenTokenCompositeSearchParam with non-null systemId value
EXEC dbo.LogSchemaMigrationProgress 'Back-fill column SystemId1 and SystemId2 into the table dbo.QuantitySearchParam';

UPDATE dbo.TokenTokenCompositeSearchParam
SET SystemId1 = 0 
WHERE SystemId1 IS NULL;

UPDATE dbo.TokenTokenCompositeSearchParam
SET SystemId2 = 0 
WHERE SystemId2 IS NULL;

GO

-- Update LowValue, HighValue, QuantityCodeId2, SystemId1 and SystemId2 columns as NOT NULL and Add default constraint to systemId1
IF ((SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenTokenCompositeSearchParam', 'U'), 'SystemId1', 'AllowsNull')) = 1
        OR (SELECT COLUMNPROPERTY(OBJECT_ID('dbo.TokenTokenCompositeSearchParam', 'U'), 'SystemId2', 'AllowsNull')) = 1)
BEGIN
    -- Drop indexes that uses SystemId1 and SystemId2 values
	IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenTokenCompositeSearchParam_Code1_Code2')
	BEGIN
	    EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenTokenCompositeSearchParam_Code1_Code2';
		DROP INDEX IX_TokenTokenCompositeSearchParam_Code1_Code2
		ON dbo.TokenTokenCompositeSearchParam
	END;

    -- Update SystemId1 and SystemId2 column as non-nullable 
    EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId1 as NOT NULL';
	ALTER TABLE dbo.TokenTokenCompositeSearchParam
	ALTER COLUMN SystemId1 INT NOT NULL;

	EXEC dbo.LogSchemaMigrationProgress 'Updating SystemId2 as NOT NULL';
	ALTER TABLE dbo.TokenTokenCompositeSearchParam
	ALTER COLUMN SystemId2 INT NOT NULL;
END;
GO

-- Adding default constraint to SystemId1 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenTokenCompositeSearchParam_SystemId1' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to SystemId1 column';
    ALTER TABLE dbo.TokenTokenCompositeSearchParam
    ADD CONSTRAINT DF_TokenTokenCompositeSearchParam_SystemId1
    DEFAULT 0 FOR SystemId1;
END;
GO

-- Adding default constraint to SystemId2 column
IF NOT EXISTS (
    SELECT * 
	FROM sys.default_constraints 
	WHERE name='DF_TokenTokenCompositeSearchParam_SystemId2' AND type='D')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Add default constraint to SystemId2 column';
    ALTER TABLE dbo.TokenTokenCompositeSearchParam
    ADD CONSTRAINT DF_TokenTokenCompositeSearchParam_SystemId2
    DEFAULT 0 FOR SystemId2;
END;
GO

-- Recreate dropped indexes consisting SystemId1 and SystemId2 values
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TokenTokenCompositeSearchParam_Code1_Code2')
BEGIN
	EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenTokenCompositeSearchParam_Code1_Code2';
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
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenTokenCompositeSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenTokenCompositeSearchParam';
	ALTER TABLE dbo.TokenTokenCompositeSearchParam 
	ADD CONSTRAINT PK_TokenTokenCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceSurrogateId, SearchParamId, ResourceTypeId, SystemId1, Code1, SystemId2, Code2)
    WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END;
GO

/****************************************************************************************
 Table dbo.StringSearchParam
*****************************************************************************************/

-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.StringSearchParam'
GO
WITH cte AS (
    SELECT ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsMin, IsMax, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, SearchParamId, Text
	) row_num
	FROM dbo.StringSearchParam
)
DELETE FROM cte WHERE row_num > 1
GO

EXEC dbo.LogSchemaMigrationProgress 'Adding BulkStringSearchParamTableType_3'
IF TYPE_ID(N'BulkStringSearchParamTableType_3') IS NULL
BEGIN
    CREATE TYPE dbo.BulkStringSearchParamTableType_3 AS TABLE
    (
        Offset int NOT NULL,
        SearchParamId smallint NOT NULL,
        Text nvarchar(256) COLLATE Latin1_General_100_CI_AI_SC NOT NULL,
        TextOverflow nvarchar(max) COLLATE Latin1_General_100_CI_AI_SC NULL,
        IsMin bit NOT NULL,
        IsMax bit NOT NULL,
        TextHash binary(32) NOT NULL
    )
END
GO

/*************************************************************
Insert TextHash column and backfill for existing rows
**************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Adding TextHash column in dbo.StringSearchParam'
IF NOT EXISTS (
    SELECT * 
    FROM   sys.columns 
    WHERE  object_id = OBJECT_ID('dbo.StringSearchParam') AND name = 'TextHash')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding TextHash column as nullable';
    ALTER TABLE dbo.StringSearchParam 
        ADD TextHash binary(32) NULL 
END
GO

-- Backfill values for TextHash column and update it as NOT NULL
IF EXISTS (
    SELECT * 
    FROM   sys.columns 
    WHERE  object_id = OBJECT_ID('dbo.StringSearchParam') AND name = 'TextHash' AND is_nullable = 1)
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Backfilling values to TextHash column';
    UPDATE dbo.StringSearchParam 
        SET TextHash = (CONVERT([binary](32), (hashbytes('SHA2_256', CASE
							WHEN [TextOverflow] IS NOT NULL
							THEN [TextOverflow]
							ELSE [Text]
							END)),2))

    EXEC dbo.LogSchemaMigrationProgress 'Update TextHash as NOT NULL';
    ALTER TABLE dbo.StringSearchParam ALTER COLUMN TextHash binary(32) NOT NULL
END
GO

/*************************************************************
Add Primary key for dbo.StringSearchParam
**************************************************************/
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_StringSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PK_StringSearchParam'
	ALTER TABLE dbo.StringSearchParam 
	ADD CONSTRAINT PK_StringSearchParam PRIMARY KEY NONCLUSTERED (ResourceTypeId, ResourceSurrogateId, SearchParamId, TextHash)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
    ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
GO

/*************************************************************
    Stored procedures for creating and deleting
**************************************************************/

--
-- STORED PROCEDURE
--     UpsertResource_6
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
--     @isResourceChangeCaptureEnabled
--         * Whether capturing resource change data
--
-- RETURN VALUE
--         The version of the resource as a result set. Will be empty if no insertion was done.
--
CREATE OR ALTER PROCEDURE dbo.UpsertResource_6
@baseResourceSurrogateId BIGINT, @resourceTypeId SMALLINT, @resourceId VARCHAR (64), @eTag INT=NULL, @allowCreate BIT, @isDeleted BIT, @keepHistory BIT, @requestMethod VARCHAR (10), @searchParamHash VARCHAR (64), @rawResource VARBINARY (MAX), @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_3 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY, @isResourceChangeCaptureEnabled BIT=0
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @previousResourceSurrogateId AS BIGINT;
DECLARE @previousVersion AS BIGINT;
DECLARE @previousIsDeleted AS BIT;
SELECT @previousResourceSurrogateId = ResourceSurrogateId,
       @previousVersion = Version,
       @previousIsDeleted = IsDeleted
FROM   dbo.Resource WITH (UPDLOCK, HOLDLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND IsHistory = 0;
IF (@etag IS NOT NULL
    AND @etag <> @previousVersion)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
DECLARE @version AS INT;
IF (@previousResourceSurrogateId IS NULL)
    BEGIN
        IF (@isDeleted = 1)
            BEGIN
                COMMIT TRANSACTION;
                RETURN;
            END
        IF (@etag IS NOT NULL)
            BEGIN
                THROW 50404, 'Resource with specified version not found', 1;
            END
        IF (@allowCreate = 0)
            BEGIN
                THROW 50405, 'Resource does not exist and create is not allowed', 1;
            END
        SET @version = 1;
    END
ELSE
    BEGIN
        IF (@isDeleted = 1
            AND @previousIsDeleted = 1)
            BEGIN
                COMMIT TRANSACTION;
                RETURN;
            END
        SET @version = @previousVersion + 1;
        IF (@keepHistory = 1)
            BEGIN
                UPDATE dbo.Resource
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.CompartmentAssignment
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.ReferenceSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenText
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.StringSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.UriSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.NumberSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.QuantitySearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.DateTimeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.ReferenceTokenCompositeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenTokenCompositeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenDateTimeCompositeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenQuantityCompositeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenStringCompositeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                UPDATE dbo.TokenNumberNumberCompositeSearchParam
                SET    IsHistory = 1
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
            END
        ELSE
            BEGIN
                DELETE dbo.Resource
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.ResourceWriteClaim
                WHERE  ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.CompartmentAssignment
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.ReferenceSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenText
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.StringSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.UriSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.NumberSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.QuantitySearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.DateTimeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.ReferenceTokenCompositeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenTokenCompositeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenDateTimeCompositeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenQuantityCompositeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenStringCompositeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
                DELETE dbo.TokenNumberNumberCompositeSearchParam
                WHERE  ResourceTypeId = @resourceTypeId
                       AND ResourceSurrogateId = @previousResourceSurrogateId;
            END
    END
DECLARE @resourceSurrogateId AS BIGINT = @baseResourceSurrogateId + ( NEXT VALUE FOR ResourceSurrogateIdUniquifierSequence);
DECLARE @isRawResourceMetaSet AS BIT;
IF (@version = 1)
    BEGIN
        SET @isRawResourceMetaSet = 1;
    END
ELSE
    BEGIN
        SET @isRawResourceMetaSet = 0;
    END
INSERT  INTO dbo.Resource (ResourceTypeId, ResourceId, Version, IsHistory, ResourceSurrogateId, IsDeleted, RequestMethod, RawResource, IsRawResourceMetaSet, SearchParamHash)
VALUES                   (@resourceTypeId, @resourceId, @version, 0, @resourceSurrogateId, @isDeleted, @requestMethod, @rawResource, @isRawResourceMetaSet, @searchParamHash);
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT @resourceSurrogateId,
       ClaimTypeId,
       ClaimValue
FROM   @resourceWriteClaims;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                CompartmentTypeId,
                ReferenceResourceId,
                0
FROM   @compartmentAssignments;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri,
                ReferenceResourceTypeId,
                ReferenceResourceId,
                ReferenceResourceVersion,
                0
FROM   @referenceSearchParams;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                Code,
                0
FROM   @tokenSearchParams;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                0
FROM   @tokenTextSearchParams;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax, TextHash)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                TextOverflow,
                0,
                IsMin,
                IsMax,
                TextHash
FROM   @stringSearchParams;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Uri,
                0
FROM   @uriSearchParams;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @numberSearchParams;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                QuantityCodeId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @quantitySearchParams;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                StartDateTime,
                EndDateTime,
                IsLongerThanADay,
                0,
                IsMin,
                IsMax
FROM   @dateTimeSearchParms;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri1,
                ReferenceResourceTypeId1,
                ReferenceResourceId1,
                ReferenceResourceVersion1,
                SystemId2,
                Code2,
                0
FROM   @referenceTokenCompositeSearchParams;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                SystemId2,
                Code2,
                0
FROM   @tokenTokenCompositeSearchParams;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                StartDateTime2,
                EndDateTime2,
                IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                SingleValue2,
                SystemId2,
                QuantityCodeId2,
                LowValue2,
                HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                Text2,
                TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                SingleValue2,
                LowValue2,
                HighValue2,
                SingleValue3,
                LowValue3,
                HighValue3,
                HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams;
SELECT @version;
IF (@isResourceChangeCaptureEnabled = 1)
    BEGIN
        EXECUTE dbo.CaptureResourceChanges @isDeleted = @isDeleted, @version = @version, @resourceId = @resourceId, @resourceTypeId = @resourceTypeId;
    END
COMMIT TRANSACTION;

GO

--
-- STORED PROCEDURE
--     ReindexResource_3
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
CREATE OR ALTER PROCEDURE dbo.ReindexResource_3
@resourceTypeId SMALLINT, @resourceId VARCHAR (64), @eTag INT=NULL, @searchParamHash VARCHAR (64), @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_3 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @resourceSurrogateId AS BIGINT;
DECLARE @version AS BIGINT;
SELECT @resourceSurrogateId = ResourceSurrogateId,
       @version = Version
FROM   dbo.Resource WITH (UPDLOCK, HOLDLOCK)
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceId = @resourceId
       AND IsHistory = 0;
IF (@etag IS NOT NULL
    AND @etag <> @version)
    BEGIN
        THROW 50412, 'Precondition failed', 1;
    END
UPDATE dbo.Resource
SET    SearchParamHash = @searchParamHash
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ResourceWriteClaim
WHERE  ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.CompartmentAssignment
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ReferenceSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenText
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.StringSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.UriSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.NumberSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.QuantitySearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.DateTimeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.ReferenceTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenTokenCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenDateTimeCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenQuantityCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenStringCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
DELETE dbo.TokenNumberNumberCompositeSearchParam
WHERE  ResourceTypeId = @resourceTypeId
       AND ResourceSurrogateId = @resourceSurrogateId;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT @resourceSurrogateId,
       ClaimTypeId,
       ClaimValue
FROM   @resourceWriteClaims;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                CompartmentTypeId,
                ReferenceResourceId,
                0
FROM   @compartmentAssignments;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri,
                ReferenceResourceTypeId,
                ReferenceResourceId,
                ReferenceResourceVersion,
                0
FROM   @referenceSearchParams;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                Code,
                0
FROM   @tokenSearchParams;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                0
FROM   @tokenTextSearchParams;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax, TextHash)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Text,
                TextOverflow,
                0,
                IsMin,
                IsMax,
                TextHash
FROM   @stringSearchParams;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                Uri,
                0
FROM   @uriSearchParams;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @numberSearchParams;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId,
                QuantityCodeId,
                SingleValue,
                LowValue,
                HighValue,
                0
FROM   @quantitySearchParams;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                StartDateTime,
                EndDateTime,
                IsLongerThanADay,
                0,
                IsMin,
                IsMax
FROM   @dateTimeSearchParms;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                BaseUri1,
                ReferenceResourceTypeId1,
                ReferenceResourceId1,
                ReferenceResourceVersion1,
                SystemId2,
                Code2,
                0
FROM   @referenceTokenCompositeSearchParams;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                SystemId2,
                Code2,
                0
FROM   @tokenTokenCompositeSearchParams;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                StartDateTime2,
                EndDateTime2,
                IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                SingleValue2,
                SystemId2,
                QuantityCodeId2,
                LowValue2,
                HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                Text2,
                TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT @resourceTypeId,
                @resourceSurrogateId,
                SearchParamId,
                SystemId1,
                Code1,
                SingleValue2,
                LowValue2,
                HighValue2,
                SingleValue3,
                LowValue3,
                HighValue3,
                HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams;
COMMIT TRANSACTION;

GO

--
-- STORED PROCEDURE
--     BulkReindexResources_3
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
-- RETURN VALUE
--     The number of resources that failed to reindex due to versioning conflicts.
--
CREATE OR ALTER PROCEDURE dbo.BulkReindexResources_3
@resourcesToReindex dbo.BulkReindexResourceTableType_1 READONLY, @resourceWriteClaims dbo.BulkResourceWriteClaimTableType_1 READONLY, @compartmentAssignments dbo.BulkCompartmentAssignmentTableType_1 READONLY, @referenceSearchParams dbo.BulkReferenceSearchParamTableType_1 READONLY, @tokenSearchParams dbo.BulkTokenSearchParamTableType_1 READONLY, @tokenTextSearchParams dbo.BulkTokenTextTableType_1 READONLY, @stringSearchParams dbo.BulkStringSearchParamTableType_3 READONLY, @numberSearchParams dbo.BulkNumberSearchParamTableType_1 READONLY, @quantitySearchParams dbo.BulkQuantitySearchParamTableType_1 READONLY, @uriSearchParams dbo.BulkUriSearchParamTableType_1 READONLY, @dateTimeSearchParms dbo.BulkDateTimeSearchParamTableType_2 READONLY, @referenceTokenCompositeSearchParams dbo.BulkReferenceTokenCompositeSearchParamTableType_1 READONLY, @tokenTokenCompositeSearchParams dbo.BulkTokenTokenCompositeSearchParamTableType_1 READONLY, @tokenDateTimeCompositeSearchParams dbo.BulkTokenDateTimeCompositeSearchParamTableType_1 READONLY, @tokenQuantityCompositeSearchParams dbo.BulkTokenQuantityCompositeSearchParamTableType_1 READONLY, @tokenStringCompositeSearchParams dbo.BulkTokenStringCompositeSearchParamTableType_1 READONLY, @tokenNumberNumberCompositeSearchParams dbo.BulkTokenNumberNumberCompositeSearchParamTableType_1 READONLY
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @computedValues TABLE (
    Offset              INT          NOT NULL,
    ResourceTypeId      SMALLINT     NOT NULL,
    VersionProvided     BIGINT       NULL,
    SearchParamHash     VARCHAR (64) NOT NULL,
    ResourceSurrogateId BIGINT       NULL,
    VersionInDatabase   BIGINT       NULL);
INSERT INTO @computedValues
SELECT resourceToReindex.Offset,
       resourceToReindex.ResourceTypeId,
       resourceToReindex.ETag,
       resourceToReindex.SearchParamHash,
       resourceInDB.ResourceSurrogateId,
       resourceInDB.Version
FROM   @resourcesToReindex AS resourceToReindex
       LEFT OUTER JOIN
       dbo.Resource AS resourceInDB WITH (UPDLOCK, INDEX (IX_Resource_ResourceTypeId_ResourceId))
       ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND resourceInDB.ResourceId = resourceToReindex.ResourceId
          AND resourceInDB.IsHistory = 0;
DECLARE @versionDiff AS INT;
SET @versionDiff = (SELECT COUNT(*)
                    FROM   @computedValues
                    WHERE  VersionProvided IS NOT NULL
                           AND VersionProvided <> VersionInDatabase);
IF (@versionDiff > 0)
    BEGIN
        DELETE @computedValues
        WHERE  VersionProvided IS NOT NULL
               AND VersionProvided <> VersionInDatabase;
    END
UPDATE resourceInDB
SET    resourceInDB.SearchParamHash = resourceToReindex.SearchParamHash
FROM   @computedValues AS resourceToReindex
       INNER JOIN
       dbo.Resource AS resourceInDB
       ON resourceInDB.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND resourceInDB.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ResourceWriteClaim AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.CompartmentAssignment AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ReferenceSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenText AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.StringSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.UriSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.NumberSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.QuantitySearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.DateTimeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.ReferenceTokenCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenTokenCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenDateTimeCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenQuantityCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenStringCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
DELETE searchIndex
FROM   dbo.TokenNumberNumberCompositeSearchParam AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.ResourceTypeId = resourceToReindex.ResourceTypeId
          AND searchIndex.ResourceSurrogateId = resourceToReindex.ResourceSurrogateId;
INSERT INTO dbo.ResourceWriteClaim (ResourceSurrogateId, ClaimTypeId, ClaimValue)
SELECT DISTINCT resourceToReindex.ResourceSurrogateId,
                searchIndex.ClaimTypeId,
                searchIndex.ClaimValue
FROM   @resourceWriteClaims AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.CompartmentAssignment (ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.CompartmentTypeId,
                searchIndex.ReferenceResourceId,
                0
FROM   @compartmentAssignments AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.ReferenceSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.BaseUri,
                searchIndex.ReferenceResourceTypeId,
                searchIndex.ReferenceResourceId,
                searchIndex.ReferenceResourceVersion,
                0
FROM   @referenceSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, Code, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId,
                searchIndex.Code,
                0
FROM   @tokenSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenText (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Text,
                0
FROM   @tokenTextSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.StringSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Text, TextOverflow, IsHistory, IsMin, IsMax, TextHash)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Text,
                searchIndex.TextOverflow,
                0,
                searchIndex.IsMin,
                searchIndex.IsMax,
                searchIndex.TextHash
FROM   @stringSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.UriSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.Uri,
                0
FROM   @uriSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.NumberSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SingleValue,
                searchIndex.LowValue,
                searchIndex.HighValue,
                0
FROM   @numberSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.QuantitySearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId, QuantityCodeId, SingleValue, LowValue, HighValue, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId,
                searchIndex.QuantityCodeId,
                searchIndex.SingleValue,
                searchIndex.LowValue,
                searchIndex.HighValue,
                0
FROM   @quantitySearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay, IsHistory, IsMin, IsMax)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.StartDateTime,
                searchIndex.EndDateTime,
                searchIndex.IsLongerThanADay,
                0,
                searchIndex.IsMin,
                searchIndex.IsMax
FROM   @dateTimeSearchParms AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.ReferenceTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, BaseUri1, ReferenceResourceTypeId1, ReferenceResourceId1, ReferenceResourceVersion1, SystemId2, Code2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.BaseUri1,
                searchIndex.ReferenceResourceTypeId1,
                searchIndex.ReferenceResourceId1,
                searchIndex.ReferenceResourceVersion1,
                searchIndex.SystemId2,
                searchIndex.Code2,
                0
FROM   @referenceTokenCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenTokenCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SystemId2, Code2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.SystemId2,
                searchIndex.Code2,
                0
FROM   @tokenTokenCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenDateTimeCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, StartDateTime2, EndDateTime2, IsLongerThanADay2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.StartDateTime2,
                searchIndex.EndDateTime2,
                searchIndex.IsLongerThanADay2,
                0
FROM   @tokenDateTimeCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenQuantityCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, SystemId2, QuantityCodeId2, LowValue2, HighValue2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.SingleValue2,
                searchIndex.SystemId2,
                searchIndex.QuantityCodeId2,
                searchIndex.LowValue2,
                searchIndex.HighValue2,
                0
FROM   @tokenQuantityCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenStringCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, Text2, TextOverflow2, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.Text2,
                searchIndex.TextOverflow2,
                0
FROM   @tokenStringCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
INSERT INTO dbo.TokenNumberNumberCompositeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, SystemId1, Code1, SingleValue2, LowValue2, HighValue2, SingleValue3, LowValue3, HighValue3, HasRange, IsHistory)
SELECT DISTINCT resourceToReindex.ResourceTypeId,
                resourceToReindex.ResourceSurrogateId,
                searchIndex.SearchParamId,
                searchIndex.SystemId1,
                searchIndex.Code1,
                searchIndex.SingleValue2,
                searchIndex.LowValue2,
                searchIndex.HighValue2,
                searchIndex.SingleValue3,
                searchIndex.LowValue3,
                searchIndex.HighValue3,
                searchIndex.HasRange,
                0
FROM   @tokenNumberNumberCompositeSearchParams AS searchIndex
       INNER JOIN
       @computedValues AS resourceToReindex
       ON searchIndex.Offset = resourceToReindex.Offset;
SELECT @versionDiff;
COMMIT TRANSACTION;

GO
