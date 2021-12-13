EXEC dbo.LogSchemaMigrationProgress 'Beginning migration to version 24.';
GO

/*************************************************************
Insert TextHash column with computed values
**************************************************************/
EXEC dbo.LogSchemaMigrationProgress 'Adding TextHash column in dbo.StringSearchParam'
IF NOT EXISTS (
    SELECT * 
    FROM   sys.columns 
    WHERE  object_id = OBJECT_ID('dbo.StringSearchParam') AND name = 'TextHash')
BEGIN
    ALTER TABLE dbo.StringSearchParam 
        ADD TextHash 
        AS (CAST(hashbytes('SHA2_256', CASE
                                WHEN [TextOverflow] IS NOT NULL
                                THEN [TextOverflow]
                                ELSE [Text]
                                END
                                ) 
        AS nvarchar(32))) PERSISTED NOT NULL
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
