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
CREATE PROCEDURE dbo.RebuildIndex
    @tableName nvarchar(128),
    @indexName nvarchar(128),
    @pageCompression bit = 0
WITH EXECUTE AS SELF
AS
DECLARE @errorTxt as varchar(1000)
       ,@sql as nvarchar (1000)
       ,@isDisabled as bit
       ,@isExecuted as int

IF object_id(@tableName) IS NULL
BEGIN
    SET @errorTxt = @tableName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

SET @isDisabled = (SELECT is_disabled FROM sys.indexes WHERE object_id = object_id(@tableName) AND name = @indexName)
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
END
GO
