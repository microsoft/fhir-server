GO
CREATE OR ALTER   PROCEDURE [dbo].[ExecuteCommandForRebuildIndexes]
@Tbl VARCHAR (100), @Ind VARCHAR (100), @Cmd VARCHAR (MAX), @Pid INT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'ExecuteCommandForRebuildIndexes', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL'), @st AS DATETIME, @Retries AS INT = 0, @Action AS VARCHAR (100), @msg AS VARCHAR (1000);
RetryOnTempdbError:
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Text = @Cmd;
    SET @st = getUTCdate();
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    IF @Cmd IS NULL
        RAISERROR ('@Cmd IS NULL', 18, 127);
    SET @Action = CASE WHEN @Cmd LIKE 'UPDATE STAT%' THEN 'Update statistics' WHEN @Cmd LIKE 'CREATE%INDEX%' THEN 'Create Index' WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD' THEN 'Rebuild Index' WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint' END;
    IF @Action IS NULL
        BEGIN
            SET @msg = 'Not supported command = ' + CONVERT (VARCHAR (900), @Cmd);
            RAISERROR (@msg, 18, 127);
        END
    IF @Action = 'Create Index'
        WAITFOR DELAY '00:00:05';
    EXECUTE (@Cmd);
    SELECT @Ind,
           @Pid;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Action = @Action, @Status = 'End', @Start = @st, @Text = @Cmd;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    IF error_number() = 40544
        BEGIN
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st, @ReRaisError = 0, @Retry = @Retries;
            SET @Retries = @Retries + 1;
            IF @Tbl = 'TokenText_96'
                WAITFOR DELAY '01:00:00';
            ELSE
                WAITFOR DELAY '00:10:00';
            GOTO RetryOnTempdbError;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

