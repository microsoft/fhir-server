/*************************************************************
    This migration adds primary keys to the existing tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning migration to version 21.';
GO

-- SearchParam table
-- Adding nonclustered primary key on identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_SearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_SearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.SearchParam 
	ADD CONSTRAINT PK_SearchParam PRIMARY KEY NONCLUSTERED(SearchParamId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- ResourceType
-- Adding nonclustered primary key on identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_ResourceType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_ResourceType' AND type='PK')
BEGIN
	ALTER TABLE dbo.ResourceType 
	ADD CONSTRAINT PK_ResourceType PRIMARY KEY NONCLUSTERED(ResourceTypeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- System
-- Adding nonclustered primary key on identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_System'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_System' AND type='PK')
BEGIN
	ALTER TABLE dbo.System 
	ADD CONSTRAINT PK_System PRIMARY KEY NONCLUSTERED(SystemId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- QuantityCode
-- Adding nonclustered primary key on the identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_QuantityCode'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_QuantityCode' AND type='PK')
BEGIN
	ALTER TABLE dbo.QuantityCode 
	ADD CONSTRAINT PK_QuantityCode PRIMARY KEY NONCLUSTERED(QuantityCodeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- ClaimType
-- Adding nonclustered primary key on the identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_ClaimType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_ClaimType' AND type='PK')
BEGIN
	ALTER TABLE dbo.ClaimType 
	ADD CONSTRAINT PK_ClaimType PRIMARY KEY NONCLUSTERED(ClaimTypeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- CompartmentType
-- Create nonclustered primary key on the identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_CompartmentType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_CompartmentType' AND type='PK')
BEGIN
	ALTER TABLE dbo.CompartmentType 
	ADD CONSTRAINT PK_CompartmentType PRIMARY KEY NONCLUSTERED(CompartmentTypeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- CompartmentAssignment
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
	CREATE NONCLUSTERED INDEX IX_Resource ON dbo.Resource
	(
		ResourceTypeId,
		ResourceSurrogateId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
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
	ADD CONSTRAINT PKC_Resource PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
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
	CREATE NONCLUSTERED INDEX IX_ExportJob ON dbo.ExportJob
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
	ADD CONSTRAINT PKC_ExportJob PRIMARY KEY CLUSTERED(Id)
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
	CREATE NONCLUSTERED INDEX IX_ReindexJob ON dbo.ReindexJob
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
	ADD CONSTRAINT PKC_ReindexJob PRIMARY KEY CLUSTERED(Id)
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
	CREATE NONCLUSTERED INDEX IX_TaskInfo ON dbo.TaskInfo
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
	ADD CONSTRAINT PKC_TaskInfo PRIMARY KEY CLUSTERED(TaskId)
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

-- ReferenceSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_ReferenceSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_ReferenceSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.ReferenceSearchParam 
	ADD CONSTRAINT PK_ReferenceSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, ReferenceResourceId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
GO

--TokenSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenSearchParam 
	ADD CONSTRAINT PK_TokenSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Code, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenText
-- Adding nonclustered primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenText'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenText' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenText 
	ADD CONSTRAINT PK_TokenText PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop non-clustered index since primary key is created for same set of columns
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_TokenText_SearchParamId_Text'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenText_SearchParamId_Text' AND object_id = OBJECT_ID('dbo.TokenText'))
BEGIN
	DROP INDEX IX_TokenText_SearchParamId_Text ON dbo.TokenText
END
GO

--StringSearchParam
-- Create nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_StringSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_StringSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.StringSearchParam
	ADD CONSTRAINT PK_StringSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--UriSearchParam
-- Create nonclustered primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_UriSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_UriSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.UriSearchParam
	ADD CONSTRAINT PK_UriSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop nonclustered index since primary key is created for same set of columns
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_UriSearchParam_SearchParamId_Uri'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_UriSearchParam_SearchParamId_Uri' AND object_id = OBJECT_ID('dbo.UriSearchParam'))
BEGIN
	DROP INDEX IX_UriSearchParam_SearchParamId_Uri ON dbo.UriSearchParam
END
GO

--DateTimeSearchParam
-- Since we need to create primary key on the same of columns for which clustered index exists.
-- We will create non-clustered index(IX) first, drop IXC, create PKC and finally drop IX
-- so that the table would never be left without indexes.
-- Creating temporary non-clustered index
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_DateTimeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_DateTimeSearchParam' AND object_id = OBJECT_ID('dbo.DateTimeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_DateTimeSearchParam ON dbo.DateTimeSearchParam
	(
		ResourceTypeId,
        ResourceSurrogateId,
        SearchParamId
	)
	WITH (ONLINE=ON)
END

-- Drop clustered index since primary key will be created for the same set of columns
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_DateTimeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_DateTimeSearchParam' AND object_id = OBJECT_ID('dbo.DateTimeSearchParam'))
BEGIN
	DROP INDEX IXC_DateTimeSearchParam ON dbo.DateTimeSearchParam
	WITH (ONLINE=ON)
END

-- Adding primary key on the set of non-nullable columns which makes it unique.
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_DateTimeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_DateTimeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.DateTimeSearchParam
	ADD CONSTRAINT PKC_DateTimeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Deleting temporary non-clustered index created.
EXEC dbo.LogSchemaMigrationProgress 'Dropping IX_DateTimeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_DateTimeSearchParam' AND object_id = OBJECT_ID('dbo.DateTimeSearchParam'))
BEGIN
	DROP INDEX IX_DateTimeSearchParam ON dbo.DateTimeSearchParam
END
GO

--ReferenceTokenCompositeSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_ReferenceTokenCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_ReferenceTokenCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.ReferenceTokenCompositeSearchParam
	ADD CONSTRAINT PK_ReferenceTokenCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, ReferenceResourceId1, Code2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenTokenCompositeSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenTokenCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenTokenCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenTokenCompositeSearchParam
	ADD CONSTRAINT PK_TokenTokenCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Code1, Code2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenDateTimeCompositeSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenDateTimeCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenDateTimeCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenDateTimeCompositeSearchParam
	ADD CONSTRAINT PK_TokenDateTimeCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Code1, EndDateTime2, StartDateTime2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenQuantityCompositeSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenQuantityCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenQuantityCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ADD CONSTRAINT PK_TokenQuantityCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Code1)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenStringCompositeSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenStringCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenStringCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenStringCompositeSearchParam
	ADD CONSTRAINT PK_TokenStringCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, SearchParamId, Code1, Text2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenNumberNumberCompositeSearchParam
-- Adding nonclustered primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PK_TokenNumberNumberCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PK_TokenNumberNumberCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ADD CONSTRAINT PK_TokenNumberNumberCompositeSearchParam PRIMARY KEY NONCLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Code1)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO
