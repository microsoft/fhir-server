--
-- STORED PROCEDURE
--     UpdateSqlStatistics
--
-- DESCRIPTION
--     Updates statistics on all user tables in the database.
--     This procedure runs UPDATE STATISTICS on all user tables to help improve query performance.
--
-- NOTES
--     This is a potentially long-running operation and should be run during periods of low activity.
--
CREATE PROCEDURE dbo.UpdateSqlStatistics
AS
SET NOCOUNT ON;

DECLARE @SP AS VARCHAR(100) = 'UpdateSqlStatistics'
       ,@Mode AS VARCHAR(100) = ''
       ,@st AS DATETIME = getUTCdate()
       ,@TableName AS SYSNAME
       ,@SQL AS NVARCHAR(500)
       ,@TotalTables AS INT = 0
       ,@UpdatedTables AS INT = 0

BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Start', @Mode = @Mode;

    -- Get count of user tables
    SELECT @TotalTables = COUNT(*)
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'dbo'
      AND t.type = 'U'
      AND t.is_ms_shipped = 0;

    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Info', @Mode = @Mode, @Target = '@TotalTables', @Text = @TotalTables;

    -- Cursor to iterate through all user tables
    DECLARE table_cursor CURSOR FOR
    SELECT t.name
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'dbo'
      AND t.type = 'U'
      AND t.is_ms_shipped = 0
    ORDER BY t.name;

    OPEN table_cursor;
    FETCH NEXT FROM table_cursor INTO @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @SQL = 'UPDATE STATISTICS dbo.' + QUOTENAME(@TableName);
          BEGIN TRY
            EXECUTE sp_executesql @SQL;
            SET @UpdatedTables = @UpdatedTables + 1;
            EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Info', @Mode = @Mode, @Target = @TableName, @Action = 'UPDATE STATISTICS', @Text = 'Success';
        END TRY
        BEGIN CATCH
            EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Warn', @Mode = @Mode, @Target = @TableName, @Action = 'UPDATE STATISTICS', @Text = error_message;
        END CATCH

        FETCH NEXT FROM table_cursor INTO @TableName;
    END

    CLOSE table_cursor;
    DEALLOCATE table_cursor;

    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'End', @Mode = @Mode, @Start = @st, @Rows = @UpdatedTables, @Text = @UpdatedTables;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('global', 'table_cursor') >= 0
    BEGIN
        CLOSE table_cursor;
        DEALLOCATE table_cursor;
    END
    
    IF ERROR_NUMBER() = 1750 
        THROW; -- Real error is before 1750, cannot trap in SQL.
    
    EXECUTE dbo.LogEvent @Process = @SP, @Status = 'Error', @Mode = @Mode, @Start = @st;
    THROW;
END CATCH
GO
