--
-- STORED PROCEDURE
--     UpdateSqlStatistics
--
-- DESCRIPTION
--     Updates statistics on all user tables in the database to improve query performance.
--     This procedure iterates through all user tables and executes UPDATE STATISTICS command.
--
-- PARAMETERS
--     None
--
-- RETURN VALUE
--     None
--
GO
CREATE PROCEDURE dbo.UpdateSqlStatistics
AS
SET NOCOUNT ON;

DECLARE @SP VARCHAR(100) = 'UpdateSqlStatistics';
DECLARE @Mode VARCHAR(100) = 'UPDATE_STATISTICS';
DECLARE @st DATETIME = GETUTCDATE();
DECLARE @TablesProcessed INT = 0;

DECLARE @TableName NVARCHAR(128);
DECLARE @SQL NVARCHAR(500);

DECLARE table_cursor CURSOR FOR
    SELECT t.name
    FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = 'dbo'
    AND t.type = 'U'  -- User tables only
    ORDER BY t.name;

BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';

    OPEN table_cursor;
    FETCH NEXT FROM table_cursor INTO @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @SQL = 'UPDATE STATISTICS dbo.' + QUOTENAME(@TableName);
        
        BEGIN TRY
            EXEC sp_executesql @SQL;
            SET @TablesProcessed = @TablesProcessed + 1;
        END TRY
        BEGIN CATCH
            -- Log the error for this specific table but continue processing other tables
            DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Warning', 
                    @Text = 'Failed to update statistics for table: ' + @TableName + '. Error: ' + @ErrorMessage;
        END CATCH

        FETCH NEXT FROM table_cursor INTO @TableName;
    END;

    CLOSE table_cursor;
    DEALLOCATE table_cursor;

    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @TablesProcessed;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('global', 'table_cursor') >= 0
    BEGIN
        CLOSE table_cursor;
        DEALLOCATE table_cursor;
    END;

    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error';
    THROW;
END CATCH
GO
