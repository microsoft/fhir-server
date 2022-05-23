
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
CREATE PROCEDURE dbo.DisableIndex
    @tableName nvarchar(128),
    @indexName nvarchar(128)
WITH EXECUTE AS 'dbo'
AS
DECLARE @errorTxt as varchar(1000)
       ,@sql as nvarchar (1000)
       ,@isDisabled as bit

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

IF @isDisabled = 0
BEGIN
    SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Disable'
    EXECUTE sp_executesql @sql
END
GO
