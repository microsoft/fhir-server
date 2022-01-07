/*************************************************************
    This migration adds primary keys to the existing tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 25.';
GO

-- SearchParam table
-- Dropping clustered index since primary key to create on the same column
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_SearchParam' AND object_id = OBJECT_ID('dbo.SearchParam'))
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_SearchParam'
	DROP INDEX IXC_SearchParam ON dbo.SearchParam
	WITH (ONLINE=ON)
END

-- Adding primary key
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_SearchParam' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_SearchParam'
	ALTER TABLE dbo.SearchParam 
	ADD CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED (Uri)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Adding unique constraint to the identity column
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_SearchParam_SearchParamId' AND type='UQ')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_SearchParam_SearchParamId'
	ALTER TABLE dbo.SearchParam 
	ADD CONSTRAINT UQ_SearchParam_SearchParamId UNIQUE (SearchParamId)
    WITH (ONLINE=ON) 
END

GO

-- ResourceType
-- Dropping clustered index since primary key to create on the same column
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ResourceType' AND object_id = OBJECT_ID('dbo.ResourceType'))
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ResourceType'
	DROP INDEX IXC_ResourceType ON dbo.ResourceType
	WITH (ONLINE=ON)
END

-- Adding primary key
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ResourceType' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ResourceType'
	ALTER TABLE dbo.ResourceType 
	ADD CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED (Name)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Adding unique constraint to the identity column
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_ResourceType_ResourceTypeId' AND type='UQ')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_ResourceType_ResourceTypeId'
	ALTER TABLE dbo.ResourceType 
	ADD CONSTRAINT UQ_ResourceType_ResourceTypeId UNIQUE (ResourceTypeId)
	WITH (ONLINE=ON) 
END

GO

-- System
-- Dropping clustered index since primary key to create on the same column
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_System' AND object_id = OBJECT_ID('dbo.System'))
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_System'
	DROP INDEX IXC_System ON dbo.System
	WITH (ONLINE=ON)
END

-- Adding primary key
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_System' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_System'
	ALTER TABLE dbo.System
	ADD CONSTRAINT PKC_System PRIMARY KEY CLUSTERED (Value)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Adding unique constraint to the identity column
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_System_SystemId' AND type='UQ')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_System_SystemId'
	ALTER TABLE dbo.System 
	ADD CONSTRAINT UQ_System_SystemId UNIQUE (SystemId)
	WITH (ONLINE=ON) 
END

GO

-- QuantityCode
-- Dropping clustered index since primary key to create on the same column
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_QuantityCode' AND object_id = OBJECT_ID('dbo.QuantityCode'))
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_QuantityCode'
	DROP INDEX IXC_QuantityCode ON dbo.QuantityCode
	WITH (ONLINE=ON)
END

-- Adding primary key
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_QuantityCode' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_QuantityCode'
	ALTER TABLE dbo.QuantityCode
	ADD CONSTRAINT PKC_QuantityCode PRIMARY KEY CLUSTERED (Value)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Adding unique constraint to the identity column
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_QuantityCode_QuantityCodeId' AND type='UQ')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_QuantityCode_QuantityCodeId'
	ALTER TABLE dbo.QuantityCode 
	ADD CONSTRAINT UQ_QuantityCode_QuantityCodeId UNIQUE (QuantityCodeId)
	WITH (ONLINE=ON) 
END

GO

-- ClaimType
-- Adding nonclustered primary key on the unique column
-- Dropping clustered index since primary key to create on the same column
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_Claim' AND object_id = OBJECT_ID('dbo.ClaimType'))
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ClaimType'
	DROP INDEX IXC_Claim ON dbo.ClaimType
	WITH (ONLINE=ON)
END

-- Adding primary key
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ClaimType' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ClaimType'
	ALTER TABLE dbo.ClaimType
	ADD CONSTRAINT PKC_ClaimType PRIMARY KEY CLUSTERED (Name)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Adding unique constraint to the identity column
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_ClaimType_ClaimTypeId' AND type='UQ')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_ClaimType_ClaimTypeId'
	ALTER TABLE dbo.ClaimType 
	ADD CONSTRAINT UQ_ClaimType_ClaimTypeId UNIQUE (ClaimTypeId)
	WITH (ONLINE=ON) 
END

GO

-- CompartmentType
-- Dropping clustered index since primary key to create on the same column
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_CompartmentType' AND object_id = OBJECT_ID('dbo.CompartmentType'))
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_CompartmentType'
	DROP INDEX IXC_CompartmentType ON dbo.CompartmentType
	WITH (ONLINE=ON)
END

-- Adding primary key
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_CompartmentType' AND type='PK')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_CompartmentType'
	ALTER TABLE dbo.CompartmentType
	ADD CONSTRAINT PKC_CompartmentType PRIMARY KEY CLUSTERED (Name)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Adding unique constraint to the identity column
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_CompartmentType_CompartmentTypeId' AND type='UQ')
BEGIN
    EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_CompartmentType_CompartmentTypeId'
	ALTER TABLE dbo.CompartmentType 
	ADD CONSTRAINT UQ_CompartmentType_CompartmentTypeId UNIQUE (CompartmentTypeId)
	WITH (ONLINE=ON) 
END

GO

-- CompartmentAssignment
-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.CompartmentAssignment'
GO
WITH cte AS (
    SELECT ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId, IsHistory
		ORDER BY ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId
	) row_num
	FROM dbo.CompartmentAssignment
)
DELETE FROM cte WHERE row_num > 1
GO

-- We are creating primary key on the same set of columns for which clustered index exists.
-- If script execution failed immediately after dropping clustered index, then on the same set of columns we have non clustered index for non-historical records, so we should be good.
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_CompartmentAssignment'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_CompartmentAssignment' AND object_id = OBJECT_ID('dbo.CompartmentAssignment'))
BEGIN
	DROP INDEX IXC_CompartmentAssignment ON dbo.CompartmentAssignment
	WITH (ONLINE=ON)
END

-- Add clustered primary key on the same set of columns for which clustered index just dropped above.
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_CompartmentAssignment'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_CompartmentAssignment' AND type='PK')
BEGIN
	ALTER TABLE dbo.CompartmentAssignment 
	ADD CONSTRAINT PKC_CompartmentAssignment PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId, CompartmentTypeId, ReferenceResourceId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

-- Resource
-- Since we need to create primary key on the same of columns for which clustered index exists.
-- We will create non-clustered index first, drop IXC, create PKC and finally drop IX
-- so that the table would never be left without indexes.
-- Creating temporary non-clustered index
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_Resource'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_Resource' AND object_id = OBJECT_ID('dbo.Resource'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_Resource ON dbo.Resource
	(
		ResourceTypeId,
		ResourceSurrogateId
	)
	WITH (ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Deleting clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_Resource'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_Resource' AND object_id = OBJECT_ID('dbo.Resource'))
BEGIN
	DROP INDEX IXC_Resource ON dbo.Resource
	WITH (ONLINE=ON)
END

-- Adding clustered primary key
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_Resource'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_Resource' AND type='PK')
BEGIN
	ALTER TABLE dbo.Resource 
	ADD CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED (ResourceTypeId, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId (ResourceTypeId)
END

-- Deleting temporary non-clustered index created.
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_Resource'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_Resource' AND object_id = OBJECT_ID('dbo.Resource'))
BEGIN
	DROP INDEX IX_Resource ON dbo.Resource
END

-- Adding unique constraint on ResourceSurrogateId column
EXEC dbo.LogSchemaMigrationProgress 'Adding UQ_Resource_ResourceSurrogateId'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='UQ_Resource_ResourceSurrogateId' AND type='UQ')
BEGIN
	ALTER TABLE dbo.Resource 
	ADD CONSTRAINT UQ_Resource_ResourceSurrogateId UNIQUE (ResourceSurrogateId)
	WITH (ONLINE=ON) 
	ON [Primary]
END

-- Creating UQIX_Resource_ResourceSurrogateId
EXEC dbo.LogSchemaMigrationProgress 'Creating UQIX_Resource_ResourceSurrogateId'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='UQIX_Resource_ResourceSurrogateId' AND object_id = OBJECT_ID('dbo.Resource'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX UQIX_Resource_ResourceSurrogateId ON dbo.Resource (ResourceSurrogateId)
    ON [Primary]
END

-- Dropping IX_Resource_ResourceSurrogateId since unique index for the same column created above
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_Resource_ResourceSurrogateId'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_Resource_ResourceSurrogateId' AND object_id = OBJECT_ID('dbo.Resource'))
BEGIN
	DROP INDEX IX_Resource_ResourceSurrogateId ON dbo.Resource
END

GO

-- ExportJob
-- Since we need to create primary key on the same of columns for which clustered index exists.
-- We will create non-clustered index(IX) first, drop IXC, create PKC and finally drop IX
-- so that the table would never be left without indexes.
-- Creating temporary non-clustered index
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ExportJob'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ExportJob' AND object_id = OBJECT_ID('dbo.ExportJob'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_ExportJob ON dbo.ExportJob
	(
		Id
	)
	WITH (ONLINE=ON)
END

-- Deleting clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ExportJob'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ExportJob' AND object_id = OBJECT_ID('dbo.ExportJob'))
BEGIN
	DROP INDEX IXC_ExportJob ON dbo.ExportJob
	WITH (ONLINE=ON)
END

-- Adding clustered primary key
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ExportJob'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ExportJob' AND type='PK')
BEGIN
	ALTER TABLE dbo.ExportJob 
	ADD CONSTRAINT PKC_ExportJob PRIMARY KEY CLUSTERED (Id)
	WITH (ONLINE=ON)
END

-- Deleting temporary non-clustered index created.
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_ExportJob'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ExportJob' AND object_id = OBJECT_ID('dbo.ExportJob'))
BEGIN
	DROP INDEX IX_ExportJob ON dbo.ExportJob
END
GO

-- ReindexJob
-- Since we need to create primary key on the same of columns for which clustered index exists.
-- We will create non-clustered index(IX) first, drop IXC, create PKC and finally drop IX
-- so that the table would never be left without indexes.
-- Creating temporary non-clustered index
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ReindexJob'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ReindexJob' AND object_id = OBJECT_ID('dbo.ReindexJob'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_ReindexJob ON dbo.ReindexJob
	(
		Id
	)
	WITH (ONLINE=ON)
END

EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ReindexJob'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ReindexJob' AND object_id = OBJECT_ID('dbo.ReindexJob'))
BEGIN
	DROP INDEX IXC_ReindexJob ON dbo.ReindexJob
	WITH (ONLINE=ON)
END

EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ReindexJob'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ReindexJob' AND type='PK')
BEGIN
	ALTER TABLE dbo.ReindexJob 
	ADD CONSTRAINT PKC_ReindexJob PRIMARY KEY CLUSTERED (Id)
	WITH (ONLINE=ON)
END

-- Deleting temporary non-clustered index created.
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_ReindexJob'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ReindexJob' AND object_id = OBJECT_ID('dbo.ReindexJob'))
BEGIN
	DROP INDEX IX_ReindexJob ON dbo.ReindexJob
END
GO

-- TaskInfo
-- Since we need to create primary key on the same of columns for which clustered index exists.
-- We will create non-clustered index(IX) first, drop IXC, create PKC and finally drop IX
-- so that the table would never be left without indexes.
-- Creating temporary non-clustered index
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TaskInfo'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TaskInfo' AND object_id = OBJECT_ID('dbo.TaskInfo'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_TaskInfo ON dbo.TaskInfo
	(
		TaskId
	)
	WITH (ONLINE=ON)
END

EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_Task'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_Task' AND object_id = OBJECT_ID('dbo.TaskInfo'))
BEGIN
	DROP INDEX IXC_Task ON dbo.TaskInfo
	WITH (ONLINE=ON)
END

EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TaskInfo'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TaskInfo' AND type='PK')
BEGIN
	ALTER TABLE dbo.TaskInfo 
	ADD CONSTRAINT PKC_TaskInfo PRIMARY KEY CLUSTERED (TaskId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
END

-- Deleting temporary non-clustered index created.
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TaskInfo'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TaskInfo' AND object_id = OBJECT_ID('dbo.TaskInfo'))
BEGIN
	DROP INDEX IX_TaskInfo ON dbo.TaskInfo
END
GO

--UriSearchParam
-- Deleting duplicate rows based on all columns
EXEC dbo.LogSchemaMigrationProgress 'Deleting redundant rows from dbo.UriSearchParam'
GO
WITH cte AS (
    SELECT ResourceTypeId, SearchParamId, Uri, ResourceSurrogateId, IsHistory, ROW_NUMBER() 
    OVER (
		PARTITION BY ResourceTypeId, SearchParamId, Uri, ResourceSurrogateId, IsHistory
		ORDER BY ResourceTypeId, SearchParamId, Uri, ResourceSurrogateId
	) row_num
	FROM dbo.UriSearchParam
)
DELETE FROM cte WHERE row_num > 1
GO

-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_UriSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_UriSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.UriSearchParam
	ADD CONSTRAINT PK_UriSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Uri, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
GO
