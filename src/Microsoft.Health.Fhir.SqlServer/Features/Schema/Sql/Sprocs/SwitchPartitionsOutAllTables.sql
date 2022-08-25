GO
CREATE OR ALTER   PROCEDURE [dbo].[SwitchPartitionsOutAllTables]
@RebuildClustered BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsOutAllTables', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId ND=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Tables TABLE (
        name      VARCHAR (100) PRIMARY KEY,
        supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = @RebuildClustered, @IncludeNotSupported = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Tables)
        BEGIN
            SET @Tbl = (SELECT   TOP 1 name
                        FROM     @Tables
                        ORDER BY name);
            EXECUTE dbo.SwitchPartitionsOut @Tbl = @Tbl, @RebuildClustered = @RebuildClustered;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = 'SwitchPartitionsOut', @Action = 'Execute', @Text = @Tbl;
            DELETE @Tables
            WHERE  name = @Tbl;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
