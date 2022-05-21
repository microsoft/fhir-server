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
DECLARE @IsExecuted AS INT;
SET @IsExecuted = 0;
IF EXISTS (SELECT *
           FROM   [sys].[indexes]
           WHERE  name = @indexName
                  AND object_id = OBJECT_ID(@tableName)
                  AND is_disabled = 1)
    BEGIN
        DECLARE @Sql AS NVARCHAR (MAX);
        IF @pageCompression = 0 
            SET @Sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Rebuild'
	    ELSE 
            SET @Sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Rebuild WITH (DATA_COMPRESSION = PAGE)'
        
        EXECUTE sp_executesql @Sql;
        SET @IsExecuted = 1;
    END
RETURN @IsExecuted;
GO

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
CREATE OR ALTER PROCEDURE [dbo].[DisableIndex]
    @tableName nvarchar(128),
    @indexName nvarchar(128)
WITH EXECUTE AS 'dbo'
AS
DECLARE @IsExecuted AS INT;
SET @IsExecuted = 0;
IF EXISTS (SELECT *
           FROM   [sys].[indexes]
           WHERE  name = @indexName
                  AND object_id = OBJECT_ID(@tableName)
                  AND is_disabled = 0)
    BEGIN
        DECLARE @Sql AS NVARCHAR (MAX);
        SET @Sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Disable';
        EXECUTE sp_executesql @Sql;
        SET @IsExecuted = 1;
    END
RETURN @IsExecuted;
GO
