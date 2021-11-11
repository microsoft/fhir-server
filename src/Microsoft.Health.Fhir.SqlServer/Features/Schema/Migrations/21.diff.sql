/*************************************************************
    This migration adds primary keys to the existing tables
**************************************************************/

EXEC dbo.LogSchemaMigrationProgress 'Beginning migration to version 21.';
GO

-- SearchParam table

-- Creating non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_SearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_SearchParam' AND object_id = OBJECT_ID('dbo.SearchParam'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_SearchParam ON dbo.SearchParam
	(
		Uri
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_SearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_SearchParam' AND object_id = OBJECT_ID('dbo.SearchParam'))
BEGIN
	DROP INDEX IXC_SearchParam ON dbo.SearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_SearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_SearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.SearchParam 
	ADD CONSTRAINT PKC_SearchParam PRIMARY KEY CLUSTERED(SearchParamId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- ResourceType
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ResourceType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ResourceType' AND object_id = OBJECT_ID('dbo.ResourceType'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_ResourceType ON dbo.ResourceType
	(
		Name
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ResourceType'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ResourceType' AND object_id = OBJECT_ID('dbo.ResourceType'))
BEGIN
	DROP INDEX IXC_ResourceType ON dbo.ResourceType
	WITH (ONLINE=ON)
END

-- Create primary key on identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ResourceType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ResourceType' AND type='PK')
BEGIN
	ALTER TABLE dbo.ResourceType 
	ADD CONSTRAINT PKC_ResourceType PRIMARY KEY CLUSTERED(ResourceTypeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- System
-- Creating non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_System'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_System' AND object_id = OBJECT_ID('dbo.System'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_System ON dbo.System
	(
		Value
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_System'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_System' AND object_id = OBJECT_ID('dbo.System'))
BEGIN
	DROP INDEX IXC_System ON dbo.System
	WITH (ONLINE=ON)
END

-- Create primary key on identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_System'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_System' AND type='PK')
BEGIN
	ALTER TABLE dbo.System 
	ADD CONSTRAINT PKC_System PRIMARY KEY CLUSTERED(SystemId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- QuantityCode
-- Creating non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_QuantityCode'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_QuantityCode' AND object_id = OBJECT_ID('dbo.QuantityCode'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_QuantityCode ON dbo.QuantityCode
	(
		Value
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_QuantityCode'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_QuantityCode' AND object_id = OBJECT_ID('dbo.QuantityCode'))
BEGIN
	DROP INDEX IXC_QuantityCode ON dbo.QuantityCode
	WITH (ONLINE=ON)
END

-- Create primary key on the identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_QuantityCode'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_QuantityCode' AND type='PK')
BEGIN
	ALTER TABLE dbo.QuantityCode 
	ADD CONSTRAINT PKC_QuantityCode PRIMARY KEY CLUSTERED(QuantityCodeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- ClaimType
-- Creating non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ClaimType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ClaimType' AND object_id = OBJECT_ID('dbo.ClaimType'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_ClaimType ON dbo.ClaimType
	(
		Name
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_Claim'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_Claim' AND object_id = OBJECT_ID('dbo.ClaimType'))
BEGIN
	DROP INDEX IXC_Claim ON dbo.ClaimType
	WITH (ONLINE=ON)
END

-- Add primary key on the identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ClaimType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ClaimType' AND type='PK')
BEGIN
	ALTER TABLE dbo.ClaimType 
	ADD CONSTRAINT PKC_ClaimType PRIMARY KEY CLUSTERED(ClaimTypeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- CompartmentType
-- Creating non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_CompartmentType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_CompartmentType' AND object_id = OBJECT_ID('dbo.CompartmentType'))
BEGIN
	CREATE UNIQUE NONCLUSTERED INDEX IX_CompartmentType ON dbo.CompartmentType
	(
		Name
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_CompartmentType'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_CompartmentType' AND object_id = OBJECT_ID('dbo.CompartmentType'))
BEGIN
	DROP INDEX IXC_CompartmentType ON dbo.CompartmentType
	WITH (ONLINE=ON)
END

-- Create primary key on the identity column
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_CompartmentType'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_CompartmentType' AND type='PK')
BEGIN
	ALTER TABLE dbo.CompartmentType 
	ADD CONSTRAINT PKC_CompartmentType PRIMARY KEY CLUSTERED(CompartmentTypeId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
END

GO

-- CompartmentAssignment
-- We don't need to add non-clustered index for the dropping clustered index since in the next step we are creating primary key on the same set of columns.
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

-- Add primary key on the same set of columns for which clustered index just dropped above.
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
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_Resource'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_Resource' AND object_id = OBJECT_ID('dbo.Resource'))
BEGIN
	DROP INDEX IXC_Resource ON dbo.Resource
	WITH (ONLINE=ON)
END

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

GO

-- ExportJob
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ExportJob'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ExportJob' AND object_id = OBJECT_ID('dbo.ExportJob'))
BEGIN
	DROP INDEX IXC_ExportJob ON dbo.ExportJob
	WITH (ONLINE=ON)
END

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

GO

-- ReindexJob
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

GO

-- TaskInfo
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

GO

-- ReferenceSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ReferenceSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ReferenceSearchParam' AND object_id = OBJECT_ID('dbo.ReferenceSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_ReferenceSearchParam ON dbo.ReferenceSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ReferenceSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ReferenceSearchParam' AND object_id = OBJECT_ID('dbo.ReferenceSearchParam'))
BEGIN
	DROP INDEX IXC_ReferenceSearchParam ON dbo.ReferenceSearchParam
	WITH (ONLINE=ON)
END

-- Add primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ReferenceSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ReferenceSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.ReferenceSearchParam 
	ADD CONSTRAINT PKC_ReferenceSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, ReferenceResourceId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END
GO

--TokenSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenSearchParam' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenSearchParam ON dbo.TokenSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered primary key
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenSearchParam' AND object_id = OBJECT_ID('dbo.TokenSearchParam'))
BEGIN
	DROP INDEX IXC_TokenSearchParam ON dbo.TokenSearchParam
	WITH (ONLINE=ON)
END

-- Add clustered primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenSearchParam 
	ADD CONSTRAINT PKC_TokenSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, Code, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenText
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenText'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenText' AND object_id = OBJECT_ID('dbo.TokenText'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenText ON dbo.TokenText
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenText'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenText' AND object_id = OBJECT_ID('dbo.TokenText'))
BEGIN
	DROP INDEX IXC_TokenText ON dbo.TokenText
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenText'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenText' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenText 
	ADD CONSTRAINT PKC_TokenText PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId)
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
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_StringSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_StringSearchParam' AND object_id = OBJECT_ID('dbo.StringSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_StringSearchParam ON dbo.StringSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_StringSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_StringSearchParam' AND object_id = OBJECT_ID('dbo.StringSearchParam'))
BEGIN
	DROP INDEX IXC_StringSearchParam ON dbo.StringSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_StringSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_StringSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.StringSearchParam
	ADD CONSTRAINT PKC_StringSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, Text, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--UriSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_UriSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_UriSearchParam' AND object_id = OBJECT_ID('dbo.UriSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_UriSearchParam ON dbo.UriSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_UriSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_UriSearchParam' AND object_id = OBJECT_ID('dbo.UriSearchParam'))
BEGIN
	DROP INDEX IXC_UriSearchParam ON dbo.UriSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns that makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_UriSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_UriSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.UriSearchParam
	ADD CONSTRAINT PKC_UriSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Uri)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop non-clustered index since primary key is created for same set of columns
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

-- Create primary key on the set of non-nullable columns which makes it unique.
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

GO

--ReferenceTokenCompositeSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_ReferenceTokenCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_ReferenceTokenCompositeSearchParam' AND object_id = OBJECT_ID('dbo.ReferenceTokenCompositeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_ReferenceTokenCompositeSearchParam ON dbo.ReferenceTokenCompositeSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_ReferenceTokenCompositeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_ReferenceTokenCompositeSearchParam' AND object_id = OBJECT_ID('dbo.ReferenceTokenCompositeSearchParam'))
BEGIN
	DROP INDEX IXC_ReferenceTokenCompositeSearchParam ON dbo.ReferenceTokenCompositeSearchParam
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_ReferenceTokenCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_ReferenceTokenCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.ReferenceTokenCompositeSearchParam
	ADD CONSTRAINT PKC_ReferenceTokenCompositeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, ReferenceResourceId1, Code2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenTokenCompositeSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenTokenCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenTokenCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenTokenCompositeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenTokenCompositeSearchParam ON dbo.TokenTokenCompositeSearchParam
	(
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenTokenCompositeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenTokenCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenTokenCompositeSearchParam'))
BEGIN
	DROP INDEX IXC_TokenTokenCompositeSearchParam ON dbo.TokenTokenCompositeSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenTokenCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenTokenCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenTokenCompositeSearchParam
	ADD CONSTRAINT PKC_TokenTokenCompositeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, Code1, Code2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenDateTimeCompositeSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenDateTimeCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenDateTimeCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenDateTimeCompositeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenDateTimeCompositeSearchParam ON dbo.TokenDateTimeCompositeSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenDateTimeCompositeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenDateTimeCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenDateTimeCompositeSearchParam'))
BEGIN
	DROP INDEX IXC_TokenDateTimeCompositeSearchParam ON dbo.TokenDateTimeCompositeSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenDateTimeCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenDateTimeCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenDateTimeCompositeSearchParam
	ADD CONSTRAINT PKC_TokenDateTimeCompositeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, Code1, EndDateTime2, StartDateTime2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenQuantityCompositeSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenQuantityCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenQuantityCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenQuantityCompositeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenQuantityCompositeSearchParam ON dbo.TokenQuantityCompositeSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenQuantityCompositeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenQuantityCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenQuantityCompositeSearchParam'))
BEGIN
	DROP INDEX IXC_TokenQuantityCompositeSearchParam ON dbo.TokenQuantityCompositeSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenQuantityCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenQuantityCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenQuantityCompositeSearchParam
	ADD CONSTRAINT PKC_TokenQuantityCompositeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Code1)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenStringCompositeSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenStringCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenStringCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenStringCompositeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenStringCompositeSearchParam ON dbo.TokenStringCompositeSearchParam
	(
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenStringCompositeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenStringCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenStringCompositeSearchParam'))
BEGIN
	DROP INDEX IXC_TokenStringCompositeSearchParam ON dbo.TokenStringCompositeSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenStringCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenStringCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenStringCompositeSearchParam
	ADD CONSTRAINT PKC_TokenStringCompositeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, SearchParamId, Code1, Text2, ResourceSurrogateId)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO

--TokenNumberNumberCompositeSearchParam
-- Create non-clustered index which was clustered index since the clustered index will be dropped in next step
EXEC dbo.LogSchemaMigrationProgress 'Creating IX_TokenNumberNumberCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IX_TokenNumberNumberCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam'))
BEGIN
	CREATE NONCLUSTERED INDEX IX_TokenNumberNumberCompositeSearchParam ON dbo.TokenNumberNumberCompositeSearchParam
	(
		ResourceTypeId,
		ResourceSurrogateId,
		SearchParamId
	)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON)
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

-- Drop clustered index
EXEC dbo.LogSchemaMigrationProgress 'Dropping IXC_TokenNumberNumberCompositeSearchParam'
IF EXISTS (
    SELECT * 
	FROM sys.indexes 
	WHERE name='IXC_TokenNumberNumberCompositeSearchParam' AND object_id = OBJECT_ID('dbo.TokenNumberNumberCompositeSearchParam'))
BEGIN
	DROP INDEX IXC_TokenNumberNumberCompositeSearchParam ON dbo.TokenNumberNumberCompositeSearchParam
	WITH (ONLINE=ON)
END

-- Create primary key on the set of non-nullable columns which makes it unique
EXEC dbo.LogSchemaMigrationProgress 'Adding PKC_TokenNumberNumberCompositeSearchParam'
IF NOT EXISTS (
    SELECT * 
	FROM sys.key_constraints 
	WHERE name='PKC_TokenNumberNumberCompositeSearchParam' AND type='PK')
BEGIN
	ALTER TABLE dbo.TokenNumberNumberCompositeSearchParam
	ADD CONSTRAINT PKC_TokenNumberNumberCompositeSearchParam PRIMARY KEY CLUSTERED(ResourceTypeId, ResourceSurrogateId, SearchParamId, Code1)
	WITH (DATA_COMPRESSION = PAGE, ONLINE=ON) 
	ON PartitionScheme_ResourceTypeId(ResourceTypeId)
END

GO
