/*************************************************************
    This migration removes primary key from few tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 26.';
GO

/*************************************************************
        QuantitySearchParam table
**************************************************************/
-- Dropping primary key
IF EXISTS (
    SELECT *
	FROM sys.key_constraints
	WHERE name='PK_QuantitySearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping PK_QuantitySearchParam'
    ALTER TABLE dbo.QuantitySearchParam DROP CONSTRAINT PK_QuantitySearchParam;
END;
GO

/*************************************************************
        NumberSearchParam table
**************************************************************/
-- Dropping primary key
IF EXISTS (
    SELECT *
	FROM sys.key_constraints
	WHERE name='PK_NumberSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping PK_NumberSearchParam'
    ALTER TABLE dbo.NumberSearchParam DROP CONSTRAINT PK_NumberSearchParam;
END;
GO

/*************************************************************
        UriSearchParam table
**************************************************************/
-- Dropping primary key
IF EXISTS (
    SELECT *
	FROM sys.key_constraints
	WHERE name='PK_UriSearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping PK_QuantitySearchParam'
    ALTER TABLE dbo.UriSearchParam DROP CONSTRAINT PK_UriSearchParam;
END;
GO

/*************************************************************
        Resource table
**************************************************************/
-- Dropping unique constraint on ResourceSurrogateId column since unique index already exists on this column
IF EXISTS (
    SELECT *
	FROM sys.objects
	WHERE name='UQ_Resource_ResourceSurrogateId' AND type = 'UQ' AND OBJECT_NAME(parent_object_id) = N'Resource')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping UQ_Resource_ResourceSurrogateId'
    ALTER TABLE dbo.Resource DROP CONSTRAINT UQ_Resource_ResourceSurrogateId;
END;
GO

-- Rename unique index for consistency
IF EXISTS (
    SELECT *
	FROM sys.indexes
	WHERE name='UQIX_Resource_ResourceSurrogateId')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Renaming UQIX_Resource_ResourceSurrogateId to IX_Resource_ResourceSurrogateId'
    EXEC sp_rename N'dbo.Resource.UQIX_Resource_ResourceSurrogateId', N'IX_Resource_ResourceSurrogateId', N'INDEX';
END;
GO
