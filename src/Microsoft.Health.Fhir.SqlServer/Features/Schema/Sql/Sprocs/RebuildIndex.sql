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
CREATE PROCEDURE [dbo].[RebuildIndex]
    @tableName nvarchar(128),
    @indexName nvarchar(128)
AS
    SET NOCOUNT ON
    SET XACT_ABORT ON

    SET TRANSACTION ISOLATION LEVEL SERIALIZABLE

    DECLARE @IsExecuted INT
    SET @IsExecuted = 0

    BEGIN TRANSACTION

    IF EXISTS
    (
        SELECT *
        FROM [sys].[indexes]
        WHERE name = @indexName
        AND object_id = OBJECT_ID(@tableName)
        AND is_disabled = 1
    )
    BEGIN
        DECLARE @Sql NVARCHAR(MAX);

        SET @Sql = N'ALTER INDEX ' +  QUOTENAME(@indexName)
        + N' on ' + @tableName + ' Rebuild'

        EXECUTE sp_executesql @Sql

        SET @IsExecuted = 1
    END

    COMMIT TRANSACTION

    RETURN @IsExecuted
GO
