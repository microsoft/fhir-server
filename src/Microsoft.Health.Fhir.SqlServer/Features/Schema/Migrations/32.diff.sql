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
AS
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
DECLARE @IsExecuted AS INT;
SET @IsExecuted = 0;
BEGIN TRANSACTION;
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
COMMIT TRANSACTION;
RETURN @IsExecuted;
GO
