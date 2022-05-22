
/*************************************************************
    Stored procedures for disable index
**************************************************************/
--
-- STORED PROCEDURE
--     DisableIndex
--
-- DESCRIPTION
--     Stored procedures for disable index
--
-- PARAMETERS
--     @tableName
--         * index table name
--     @indexName
--         * index name

GO
CREATE OR ALTER PROCEDURE [dbo].[DisableIndex]
    @tableName nvarchar(128),
    @indexName nvarchar(128)
WITH EXECUTE AS 'dbo'
AS
DECLARE @errorTxt as varchar(1000)
       ,@sql as nvarchar (1000)
       ,@isDisabled as bit = 0
       ,@isExecuted as int = 0

IF object_id(@tableName) IS NULL
BEGIN
    SET @errorTxt = @tableName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

SELECT TOP 1 @isDisabled = is_disabled FROM sys.indexes WHERE object_id = object_id(@tableName) AND name = @indexName
IF @isDisabled IS NULL
BEGIN
    SET @errorTxt = @indexName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

IF @isDisabled = 0
BEGIN
    SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Disable'
    EXECUTE sp_executesql @sql
    
    SET @isExecuted = 1
END
RETURN @isExecuted
GO

/*************************************************************
    Stored procedures for rebuild index
**************************************************************/
--
-- STORED PROCEDURE
--     RebuildIndex
--
-- DESCRIPTION
--     Stored procedures for rebuild index
--
-- PARAMETERS
--     @tableName
--         * index table name
--     @indexName
--         * index name
--     @pageCompression
--         * index page compression

GO
CREATE OR ALTER PROCEDURE [dbo].[RebuildIndex_2]
    @tableName nvarchar(128),
    @indexName nvarchar(128),
    @pageCompression bit
WITH EXECUTE AS 'dbo'
AS
DECLARE @errorTxt as varchar(1000)
       ,@sql as nvarchar (1000)
       ,@isDisabled as bit = 0
       ,@isExecuted as int = 0

IF object_id(@tableName) IS NULL
BEGIN
    SET @errorTxt = @tableName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

SELECT TOP 1 @isDisabled = is_disabled FROM sys.indexes WHERE object_id = object_id(@tableName) AND name = @indexName
IF @isDisabled IS NULL
BEGIN
    SET @errorTxt = @indexName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

IF @isDisabled = 1
BEGIN
	IF @pageCompression = 0 
	BEGIN
		SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Rebuild'
	END
	ELSE 
	BEGIN
		SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Rebuild WITH (DATA_COMPRESSION = PAGE)'
	END

	EXECUTE sp_executesql @sql
	SET @isExecuted = 1
END
        
RETURN @isExecuted
GO
