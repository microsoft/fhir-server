GO
CREATE OR ALTER   PROCEDURE [dbo].[SwitchPartitionsIn]
@Tbl VARCHAR (100)
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsIn', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL'), @st AS DATETIME = getUTCdate(), @ResourceTypeId AS SMALLINT, @Rows AS BIGINT, @Txt AS VARCHAR (1000), @TblInt AS VARCHAR (100), @Ind AS VARCHAR (200), @IndId AS INT;
DECLARE @Indexes TABLE (
    IndId INT           PRIMARY KEY,
    name  VARCHAR (200));
DECLARE @ResourceTypes TABLE (
    ResourceTypeId SMALLINT PRIMARY KEY);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    INSERT INTO @Indexes
    SELECT index_id,
           name
    FROM   sys.indexes
    WHERE  object_id = object_id(@Tbl)
           AND is_disabled = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Indexes)
        BEGIN
            SELECT   TOP 1 @IndId = IndId,
                           @Ind = name
            FROM     @Indexes
            ORDER BY IndId;
            SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''' + @Tbl + ''') AND name = ''' + @Ind + ''' AND is_disabled = 1) ALTER INDEX ' + @Ind + ' ON dbo.' + @Tbl + ' REBUILD';
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Ind, @Action = 'Rebuild', @Text = @Txt;
            DELETE @Indexes
            WHERE  IndId = @IndId;
        END
    INSERT INTO @ResourceTypes
    SELECT CONVERT (SMALLINT, substring(name, charindex('_', name) + 1, 6)) AS ResourceTypeId
    FROM   sys.objects AS O
    WHERE  name LIKE @Tbl + '[_]%'
           AND EXISTS (SELECT *
                       FROM   sysindexes
                       WHERE  id = O.object_id
                              AND indid IN (0, 1)
                              AND rows > 0);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '#ResourceTypes', @Action = 'Select Into', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @ResourceTypes)
        BEGIN
            SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId
                                   FROM   @ResourceTypes);
            SET @TblInt = @Tbl + '_' + CONVERT (VARCHAR, @ResourceTypeId);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt;
            SET @Txt = 'ALTER TABLE dbo.' + @TblInt + ' SWITCH TO dbo.' + @Tbl + ' PARTITION $partition.PartitionFunction_ResourceTypeId(' + CONVERT (VARCHAR, @ResourceTypeId) + ')';
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch in start', @Text = @Txt;
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch in', @Text = @Txt;
            IF EXISTS (SELECT *
                       FROM   sysindexes
                       WHERE  id = object_id(@TblInt)
                              AND rows > 0)
                BEGIN
                    SET @Txt = @TblInt + ' is not empty after switch';
                    RAISERROR (@Txt, 18, 127);
                END
            EXECUTE ('DROP TABLE dbo.' + @TblInt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Drop';
            DELETE @ResourceTypes
            WHERE  ResourceTypeId = @ResourceTypeId;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH
