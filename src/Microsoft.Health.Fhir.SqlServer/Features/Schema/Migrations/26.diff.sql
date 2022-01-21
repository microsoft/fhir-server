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
