GO
CREATE OR ALTER   PROCEDURE [dbo].[GetCommandsForRebuildIndexes]
@RebuildClustered BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetCommandsForRebuildIndexes', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId RC=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100), @TblInt AS VARCHAR (100), @Ind AS VARCHAR (200), @IndId AS INT, @Supported AS BIT, @Txt AS VARCHAR (MAX), @Rows AS BIGINT, @Pages AS BIGINT, @ResourceTypeId AS SMALLINT, @IndexesCnt AS INT;
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Commands TABLE (
        Tbl   VARCHAR (100),
        Ind   VARCHAR (200),
        Txt   VARCHAR (MAX),
        Pid   INT          ,
        Pages BIGINT       );
    DECLARE @ResourceTypes TABLE (
        ResourceTypeId SMALLINT PRIMARY KEY);
    DECLARE @Indexes TABLE (
        Ind   VARCHAR (200) PRIMARY KEY,
        IndId INT          );
    DECLARE @Tables TABLE (
        name      VARCHAR (100) PRIMARY KEY,
        Supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Tables)
        BEGIN
            SELECT   TOP 1 @Tbl = name,
                           @Supported = Supported
            FROM     @Tables
            ORDER BY name;
            IF @Supported = 0
                BEGIN
                    INSERT INTO @Commands
                    SELECT @Tbl,
                           name,
                           'ALTER INDEX ' + name + ' ON dbo.' + @Tbl + ' REBUILD',
                           0,
                           CONVERT (BIGINT, 9e18)
                    FROM   sys.indexes
                    WHERE  object_id = object_id(@Tbl)
                           AND (is_disabled = 1
                                AND index_id > 1
                                AND @RebuildClustered = 0
                                OR index_id = 1
                                   AND @RebuildClustered = 1);
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Commands', @Action = 'Insert', @Rows = @@rowcount, @Text = 'Not supported tables with disabled indexes';
                END
            ELSE
                BEGIN
                    DELETE @ResourceTypes;
                    INSERT INTO @ResourceTypes
                    SELECT CONVERT (SMALLINT, substring(name, charindex('_', name) + 1, 6)) AS ResourceTypeId
                    FROM   sys.sysobjects
                    WHERE  name LIKE @Tbl + '[_]%';
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@ResourceTypes', @Action = 'Insert', @Rows = @@rowcount;
                    WHILE EXISTS (SELECT *
                                  FROM   @ResourceTypes)
                        BEGIN
                            SET @ResourceTypeId = (SELECT   TOP 1 ResourceTypeId
                                                   FROM     @ResourceTypes
                                                   ORDER BY ResourceTypeId);
                            SET @TblInt = @Tbl + '_' + CONVERT (VARCHAR, @ResourceTypeId);
                            SET @Pages = (SELECT dpages
                                          FROM   sysindexes
                                          WHERE  id = object_id(@TblInt)
                                                 AND indid IN (0, 1));
                            DELETE @Indexes;
                            INSERT INTO @Indexes
                            SELECT name,
                                   index_id
                            FROM   sys.indexes
                            WHERE  object_id = object_id(@Tbl)
                                   AND (index_id > 1
                                        AND @RebuildClustered = 0
                                        OR index_id = 1
                                           AND @RebuildClustered = 1);
                            SET @IndexesCnt = 0;
                            WHILE EXISTS (SELECT *
                                          FROM   @Indexes)
                                BEGIN
                                    SELECT   TOP 1 @Ind = Ind,
                                                   @IndId = IndId
                                    FROM     @Indexes
                                    ORDER BY Ind;
                                    IF @IndId = 1
                                        BEGIN
                                            SET @Txt = 'ALTER INDEX ' + @Ind + ' ON dbo.' + @TblInt + ' REBUILD';
                                            INSERT INTO @Commands
                                            SELECT @TblInt,
                                                   @Ind,
                                                   @Txt,
                                                   @ResourceTypeId,
                                                   @Pages;
                                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Add command', @Rows = @@rowcount, @Text = @Txt;
                                        END
                                    ELSE
                                        IF NOT EXISTS (SELECT *
                                                       FROM   sys.indexes
                                                       WHERE  object_id = object_id(@TblInt)
                                                              AND name = @Ind)
                                            BEGIN
                                                EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 0, @Txt = @Txt OUTPUT;
                                                SET @Txt = replace(@Txt, '[' + @Tbl + ']', @TblInt);
                                                IF @Txt IS NOT NULL
                                                    BEGIN
                                                        SET @IndexesCnt = @IndexesCnt + 1;
                                                        INSERT INTO @Commands
                                                        SELECT @TblInt,
                                                               @Ind,
                                                               @Txt,
                                                               @ResourceTypeId,
                                                               @Pages;
                                                        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Add command', @Rows = @@rowcount, @Text = @Txt;
                                                    END
                                            END
                                    DELETE @Indexes
                                    WHERE  Ind = @Ind;
                                END
                            IF @IndexesCnt > 1
                                BEGIN
                                    INSERT INTO @Commands
                                    SELECT @TblInt,
                                           'UPDATE STAT',
                                           'UPDATE STATISTICS dbo.' + @TblInt,
                                           @ResourceTypeId,
                                           @Pages;
                                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Add command', @Rows = @@rowcount, @Text = 'Add stats update';
                                END
                            DELETE @ResourceTypes
                            WHERE  ResourceTypeId = @ResourceTypeId;
                        END
                END
            DELETE @Tables
            WHERE  name = @Tbl;
        END
    SELECT   Tbl,
             Ind,
             Txt,
             Pid
    FROM     @Commands
    ORDER BY Pages DESC, Tbl, CASE WHEN Txt LIKE 'UPDATE STAT%' THEN 0 ELSE 1 END;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Commands', @Action = 'Select', @Rows = @@rowcount;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH

