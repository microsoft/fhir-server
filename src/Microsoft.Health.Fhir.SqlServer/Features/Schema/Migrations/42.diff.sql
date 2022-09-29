GO
CREATE OR ALTER PROCEDURE [dbo].[GetIndexCommands]
@Tbl VARCHAR (100), @Ind VARCHAR (200), @AddPartClause BIT, @IncludeClustered BIT, @Txt VARCHAR (MAX)=NULL OUTPUT
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetIndexCommands', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' Ind=' + isnull(@Ind, 'NULL'), @st AS DATETIME = getUTCdate(), @DataComp AS VARCHAR (100), @FilterDef AS VARCHAR (200), @CommandForKey AS VARCHAR (200), @CommandForInc AS VARCHAR (200), @TblId AS INT, @IndId AS INT, @colname AS VARCHAR (100), @PartClause AS VARCHAR (100);
DECLARE @KeyColsTable TABLE (
    KeyCol VARCHAR (200));
DECLARE @IncColsTable TABLE (
    IncCol VARCHAR (200));
DECLARE @Table_index TABLE (
    object_id INT,
    index_id  INT);
DECLARE @Indexes TABLE (
    Ind VARCHAR (200) PRIMARY KEY,
    Txt VARCHAR (MAX));
DECLARE @Temp TABLE (
    object_id INT          ,
    index_id  INT          ,
    KeyCols   VARCHAR (200),
    IncCols   VARCHAR (200));
BEGIN TRY
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    INSERT INTO @Table_index
    SELECT I.object_id,
           I.index_id
    FROM   sys.indexes AS I
           INNER JOIN
           sys.objects AS O
           ON I.object_id = O.object_id
    WHERE  O.name = @Tbl
           AND I.name = @Ind;
    WHILE EXISTS (SELECT *
                  FROM   @Table_index)
        BEGIN
            SELECT TOP 1 @TblId = object_id,
                         @IndId = index_id
            FROM   @Table_index;
            SET @CommandForKey = '';
            SET @CommandForInc = '';
            DELETE @KeyColsTable;
            INSERT INTO @KeyColsTable
            SELECT   C.name
            FROM     sys.index_columns AS IC
                     INNER JOIN
                     sys.indexes AS I
                     ON IC.object_id = I.object_id
                        AND IC.index_id = I.index_id, sys.columns AS C
            WHERE    C.column_id = IC.column_id
                     AND C.object_id = IC.object_id
                     AND IC.object_id = @TblId
                     AND IC.index_id = @IndId
                     AND IC.key_ordinal > 0
                     AND IC.is_included_column = 0
            ORDER BY key_ordinal;
            WHILE EXISTS (SELECT *
                          FROM   @KeyColsTable)
                BEGIN
                    SELECT TOP 1 @colname = KeyCol
                    FROM   @KeyColsTable;
                    SET @CommandForKey = @CommandForKey + @colname + ',';
                    DELETE @KeyColsTable
                    WHERE  KeyCol = @colname;
                END
            DELETE @IncColsTable;
            INSERT INTO @IncColsTable
            SELECT   C.name
            FROM     sys.index_columns AS IC
                     INNER JOIN
                     sys.indexes AS I
                     ON IC.object_id = I.object_id
                        AND IC.index_id = I.index_id, sys.columns AS C
            WHERE    C.column_id = IC.column_id
                     AND C.object_id = IC.object_id
                     AND IC.object_id = @TblId
                     AND IC.index_id = @IndId
                     AND IC.is_included_column = 1
            ORDER BY key_ordinal;
            WHILE EXISTS (SELECT *
                          FROM   @IncColsTable)
                BEGIN
                    SELECT TOP 1 @colname = IncCol
                    FROM   @IncColsTable;
                    SET @CommandForInc = @CommandForInc + @colname + ',';
                    DELETE @IncColsTable
                    WHERE  IncCol = @colname;
                END
            SET @DataComp = isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                                    FROM   sys.partitions AS P
                                    WHERE  P.object_id = @TblId
                                           AND P.index_id = @IndId), (SELECT TOP 1 NULLIF (PropertyValue, 'NONE')
                                                                      FROM   dbo.IndexProperties, sys.objects AS O, sys.indexes AS I
                                                                      WHERE  IndexTableName = O.Name
                                                                             AND IndexName = I.Name
                                                                             AND O.name = @Tbl
                                                                             AND I.name = @Ind
                                                                             AND PropertyName = 'DATA_COMPRESSION'));
            SELECT @FilterDef = replace(replace(replace(replace(I.filter_definition, '[', ''), ']', ''), '(', ''), ')', '')
            FROM   sys.indexes AS I
            WHERE  I.object_id = @TblId
                   AND I.index_id = @IndId;
            SELECT @PartClause = CASE WHEN EXISTS (SELECT *
                                                   FROM   sys.partition_schemes AS S, sys.indexes AS I
                                                   WHERE  S.data_space_id = I.data_space_id
                                                          AND S.name = 'PartitionScheme_ResourceTypeId'
                                                          AND I.object_id = @TblId
                                                          AND I.name = @Ind) THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END;
            INSERT INTO @Indexes
            SELECT @Ind AS Ind,
                   CASE WHEN is_primary_key = 1 THEN 'ALTER TABLE dbo.[' + @Tbl + '] ADD PRIMARY KEY ' + CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END ELSE 'CREATE' + CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END + CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END + ' INDEX ' + @Ind + ' ON dbo.[' + @Tbl + ']' END + ' (' + LEFT(@CommandForKey, len(@CommandForKey) - 1) + ')' + CASE WHEN @CommandForInc <> '' THEN ' INCLUDE (' + LEFT(@CommandForInc, len(@CommandForInc) - 1) + ')' ELSE '' END + CASE WHEN @FilterDef IS NOT NULL THEN ' WHERE ' + @FilterDef ELSE '' END + CASE WHEN @DataComp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = ' + @DataComp + ')' ELSE '' END + CASE WHEN @AddPartClause = 1 THEN @PartClause ELSE '' END AS Txt
            FROM   sys.indexes AS I
                   INNER JOIN
                   sys.objects AS O
                   ON I.object_id = O.object_id
            WHERE  O.object_id = @TblId
                   AND I.index_id = @IndId
                   AND (@IncludeClustered = 1
                        OR index_id > 1);
            DELETE @Table_index
            WHERE  object_id = @TblId;
        END
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    IF @Ind IS NULL
        SELECT Ind,
               Txt
        FROM   @Indexes;
    ELSE
        SET @Txt = (SELECT Txt
                    FROM   @Indexes);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Text = @Txt;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH



GO
CREATE OR ALTER PROCEDURE [dbo].[ExecuteCommandForRebuildIndexes]
@Tbl VARCHAR (100), @Ind VARCHAR (1000), @Cmd VARCHAR (MAX)
WITH EXECUTE AS SELF
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
    SET @Action = CASE WHEN @Cmd LIKE 'UPDATE STAT%' THEN 'Update statistics' WHEN @Cmd LIKE 'CREATE%INDEX%' THEN 'Create Index' WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD%' THEN 'Rebuild Index' WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint' END;
    IF @Action IS NULL
        BEGIN
            SET @msg = 'Not supported command = ' + CONVERT (VARCHAR (900), @Cmd);
            RAISERROR (@msg, 18, 127);
        END
    IF @Action = 'Create Index'
        WAITFOR DELAY '00:00:05';
    EXECUTE (@Cmd);
    SELECT @Ind;
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


GO
CREATE OR ALTER PROCEDURE [dbo].[GetCommandsForRebuildIndexes]
@RebuildClustered BIT
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetCommandsForRebuildIndexes', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId RC=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100), @TblInt AS VARCHAR (100), @Ind AS VARCHAR (200), @IndId AS INT, @Supported AS BIT, @Txt AS VARCHAR (MAX), @Rows AS BIGINT, @Pages AS BIGINT, @ResourceTypeId AS SMALLINT, @IndexesCnt AS INT, @DataComp AS VARCHAR (100);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Commands TABLE (
        Tbl   VARCHAR (100),
        Ind   VARCHAR (200),
        Txt   VARCHAR (MAX),
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
                           'ALTER INDEX ' + name + ' ON dbo.' + @Tbl + ' REBUILD' + CASE WHEN (SELECT PropertyValue
                                                                                               FROM   dbo.IndexProperties
                                                                                               WHERE  IndexTableName = @Tbl
                                                                                                      AND IndexName = name) = 'PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END,
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
                                            SET @Txt = 'ALTER INDEX ' + @Ind + ' ON dbo.' + @TblInt + ' REBUILD' + CASE WHEN (SELECT PropertyValue
                                                                                                                              FROM   dbo.IndexProperties
                                                                                                                              WHERE  IndexTableName = @Tbl
                                                                                                                                     AND IndexName = @Ind) = 'PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END;
                                            INSERT INTO @Commands
                                            SELECT @TblInt,
                                                   @Ind,
                                                   @Txt,
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
             Txt
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

GO
CREATE OR ALTER PROCEDURE [dbo].[GetPartitionedTables]
@IncludeNotDisabled BIT, @IncludeNotSupported BIT
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetPartitionedTables', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId D=' + isnull(CONVERT (VARCHAR, @IncludeNotDisabled), 'NULL') + ' S=' + isnull(CONVERT (VARCHAR, @IncludeNotSupported), 'NULL'), @st AS DATETIME = getUTCdate();
DECLARE @NotSupportedTables TABLE (
    id INT PRIMARY KEY);
BEGIN TRY
    INSERT INTO @NotSupportedTables
    SELECT DISTINCT O.object_id
    FROM   sys.indexes AS I
           INNER JOIN
           sys.objects AS O
           ON O.object_id = I.object_id
    WHERE  O.type = 'u'
           AND EXISTS (SELECT *
                       FROM   sys.partition_schemes AS PS
                       WHERE  PS.data_space_id = I.data_space_id
                              AND name = 'PartitionScheme_ResourceTypeId')
           AND (NOT EXISTS (SELECT *
                            FROM   sys.index_columns AS IC
                                   INNER JOIN
                                   sys.columns AS C
                                   ON C.object_id = IC.object_id
                                      AND C.column_id = IC.column_id
                            WHERE  IC.object_id = I.object_id
                                   AND IC.index_id = I.index_id
                                   AND IC.key_ordinal > 0
                                   AND IC.is_included_column = 0
                                   AND C.name = 'ResourceTypeId')
                OR EXISTS (SELECT *
                           FROM   sys.indexes AS NSI
                           WHERE  NSI.object_id = O.object_id
                                  AND NOT EXISTS (SELECT *
                                                  FROM   sys.partition_schemes AS PS
                                                  WHERE  PS.data_space_id = NSI.data_space_id
                                                         AND name = 'PartitionScheme_ResourceTypeId')));
    SELECT   CONVERT (VARCHAR (100), O.name),
             CONVERT (BIT, CASE WHEN EXISTS (SELECT *
                                             FROM   @NotSupportedTables AS NSI
                                             WHERE  NSI.id = O.object_id) THEN 0 ELSE 1 END)
    FROM     sys.indexes AS I
             INNER JOIN
             sys.objects AS O
             ON O.object_id = I.object_id
    WHERE    O.type = 'u'
             AND I.index_id IN (0, 1)
             AND EXISTS (SELECT *
                         FROM   sys.partition_schemes AS PS
                         WHERE  PS.data_space_id = I.data_space_id
                                AND name = 'PartitionScheme_ResourceTypeId')
             AND EXISTS (SELECT *
                         FROM   sys.index_columns AS IC
                                INNER JOIN
                                sys.columns AS C
                                ON C.object_id = I.object_id
                                   AND C.column_id = IC.column_id
                                   AND IC.is_included_column = 0
                                   AND C.name = 'ResourceTypeId')
             AND (@IncludeNotSupported = 1
                  OR NOT EXISTS (SELECT *
                                 FROM   @NotSupportedTables AS NSI
                                 WHERE  NSI.id = O.object_id))
             AND (@IncludeNotDisabled = 1
                  OR EXISTS (SELECT *
                             FROM   sys.indexes AS D
                             WHERE  D.object_id = O.object_id
                                    AND D.is_disabled = 1))
    ORDER BY 1;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @st, @Rows = @@rowcount;
END TRY
BEGIN CATCH
    IF error_number() = 1750
        THROW;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @st;
    THROW;
END CATCH


GO
CREATE OR ALTER PROCEDURE [dbo].[SwitchPartitionsIn]
@Tbl VARCHAR (100)
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsIn', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL'), @st AS DATETIME = getUTCdate(), @ResourceTypeId AS SMALLINT, @Rows AS BIGINT, @Txt AS VARCHAR (1000), @TblInt AS VARCHAR (100), @Ind AS VARCHAR (200), @IndId AS INT, @DataComp AS VARCHAR (100);
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
            SET @DataComp = CASE WHEN (SELECT PropertyValue
                                       FROM   dbo.IndexProperties
                                       WHERE  IndexTableName = @Tbl
                                              AND IndexName = @Ind) = 'PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END;
            SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(''' + @Tbl + ''') AND name = ''' + @Ind + ''' AND is_disabled = 1) ALTER INDEX ' + @Ind + ' ON dbo.' + @Tbl + ' REBUILD' + @DataComp;
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

GO
CREATE OR ALTER PROCEDURE [dbo].[SwitchPartitionsInAllTables]
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsInAllTables', @Mode AS VARCHAR (200) = 'PS=PartitionScheme_ResourceTypeId', @st AS DATETIME = getUTCdate(), @Tbl AS VARCHAR (100);
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    DECLARE @Tables TABLE (
        name      VARCHAR (100) PRIMARY KEY,
        supported BIT          );
    INSERT INTO @Tables
    EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Tables', @Action = 'Insert', @Rows = @@rowcount;
    WHILE EXISTS (SELECT *
                  FROM   @Tables)
        BEGIN
            SET @Tbl = (SELECT   TOP 1 name
                        FROM     @Tables
                        ORDER BY name);
            EXECUTE dbo.SwitchPartitionsIn @Tbl = @Tbl;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = 'SwitchPartitionsIn', @Action = 'Execute', @Text = @Tbl;
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

GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsOut
@Tbl VARCHAR (100), @RebuildClustered BIT
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsOut', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' ND=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @ResourceTypeId AS SMALLINT, @Rows AS BIGINT, @Txt AS VARCHAR (MAX), @TblInt AS VARCHAR (100), @IndId AS INT, @Ind AS VARCHAR (200), @Name AS VARCHAR (100), @checkName AS VARCHAR (200), @definition AS VARCHAR (200);
DECLARE @Indexes TABLE (
    IndId      INT           PRIMARY KEY,
    name       VARCHAR (200),
    IsDisabled BIT          );
DECLARE @IndexesRT TABLE (
    IndId      INT           PRIMARY KEY,
    name       VARCHAR (200),
    IsDisabled BIT          );
DECLARE @ResourceTypes TABLE (
    ResourceTypeId             SMALLINT PRIMARY KEY,
    partition_number_roundtrip INT     ,
    partition_number           INT     ,
    row_count                  BIGINT  );
DECLARE @Names TABLE (
    name VARCHAR (100) PRIMARY KEY);
DECLARE @CheckConstraints TABLE (
    CheckName       VARCHAR (200),
    CheckDefinition VARCHAR (200));
BEGIN TRY
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start';
    IF @Tbl IS NULL
        RAISERROR ('@Tbl IS NULL', 18, 127);
    IF @RebuildClustered IS NULL
        RAISERROR ('@RebuildClustered IS NULL', 18, 127);
    INSERT INTO @Indexes
    SELECT index_id,
           name,
           is_disabled
    FROM   sys.indexes
    WHERE  object_id = object_id(@Tbl)
           AND (is_disabled = 0
                OR @RebuildClustered = 1);
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
    INSERT INTO @ResourceTypes
    SELECT partition_number - 1 AS ResourceTypeId,
           $PARTITION.PartitionFunction_ResourceTypeId (partition_number - 1) AS partition_number_roundtrip,
           partition_number,
           row_count
    FROM   sys.dm_db_partition_stats
    WHERE  object_id = object_id(@Tbl)
           AND index_id = 1
           AND row_count > 0;
    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@ResourceTypes', @Action = 'Insert', @Rows = @@rowcount, @Text = 'For partition switch';
    IF EXISTS (SELECT *
               FROM   @ResourceTypes
               WHERE  partition_number_roundtrip <> partition_number)
        RAISERROR ('Partition sanity check failed', 18, 127);
    WHILE EXISTS (SELECT *
                  FROM   @ResourceTypes)
        BEGIN
            SELECT   TOP 1 @ResourceTypeId = ResourceTypeId,
                           @Rows = row_count
            FROM     @ResourceTypes
            ORDER BY ResourceTypeId;
            SET @TblInt = @Tbl + '_' + CONVERT (VARCHAR, @ResourceTypeId);
            SET @Txt = 'Starting @ResourceTypeId=' + CONVERT (VARCHAR, @ResourceTypeId) + ' row_count=' + CONVERT (VARCHAR, @Rows);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Text = @Txt;
            IF NOT EXISTS (SELECT *
                           FROM   sysindexes
                           WHERE  id = object_id(@TblInt)
                                  AND rows > 0)
                BEGIN
                    IF object_id(@TblInt) IS NOT NULL
                        BEGIN
                            EXECUTE ('DROP TABLE dbo.' + @TblInt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Drop';
                        END
                    EXECUTE ('SELECT * INTO dbo.' + @TblInt + ' FROM dbo.' + @Tbl + ' WHERE 1 = 2');
                    EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Select Into', @Rows = @@rowcount;
                    DELETE @CheckConstraints;
                    INSERT INTO @CheckConstraints
                    SELECT name,
                           definition
                    FROM   sys.check_constraints
                    WHERE  parent_object_id = object_id(@Tbl);
                    WHILE EXISTS (SELECT *
                                  FROM   @CheckConstraints)
                        BEGIN
                            SELECT TOP 1 @checkName = CheckName,
                                         @definition = CheckDefinition
                            FROM   @CheckConstraints;
                            SET @Txt = 'ALTER TABLE ' + @TblInt + ' ADD CHECK ' + @definition;
                            EXECUTE (@Txt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'ALTER', @Text = @Txt;
                            DELETE @CheckConstraints
                            WHERE  CheckName = @checkName;
                        END
                    DELETE @Names;
                    INSERT INTO @Names
                    SELECT name
                    FROM   sys.columns
                    WHERE  object_id = object_id(@Tbl)
                           AND is_sparse = 1;
                    WHILE EXISTS (SELECT *
                                  FROM   @Names)
                        BEGIN
                            SET @Name = (SELECT   TOP 1 name
                                         FROM     @Names
                                         ORDER BY name);
                            SET @Txt = (SELECT 'ALTER TABLE dbo.' + @TblInt + ' ALTER COLUMN ' + @Name + ' ' + T.name + '(' + CONVERT (VARCHAR, C.precision) + ',' + CONVERT (VARCHAR, C.scale) + ') SPARSE NULL'
                                        FROM   sys.types AS T
                                               INNER JOIN
                                               sys.columns AS C
                                               ON C.system_type_id = T.system_type_id
                                        WHERE  C.object_id = object_id(@Tbl)
                                               AND C.name = @Name);
                            EXECUTE (@Txt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'ALTER', @Text = @Txt;
                            DELETE @Names
                            WHERE  name = @Name;
                        END
                END
            INSERT INTO @IndexesRT
            SELECT *
            FROM   @Indexes
            WHERE  IsDisabled = 0;
            WHILE EXISTS (SELECT *
                          FROM   @IndexesRT)
                BEGIN
                    SELECT   TOP 1 @IndId = IndId,
                                   @Ind = name
                    FROM     @IndexesRT
                    ORDER BY IndId;
                    IF NOT EXISTS (SELECT *
                                   FROM   sys.indexes
                                   WHERE  object_id = object_id(@TblInt)
                                          AND name = @Ind)
                        BEGIN
                            EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 1, @Txt = @Txt OUTPUT;
                            SET @Txt = replace(@Txt, '[' + @Tbl + ']', @TblInt);
                            EXECUTE (@Txt);
                            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @TblInt, @Action = 'Create Index', @Text = @Txt;
                        END
                    DELETE @IndexesRT
                    WHERE  IndId = @IndId;
                END
            SET @Txt = 'ALTER TABLE dbo.' + @TblInt + ' ADD CHECK (ResourceTypeId >= ' + CONVERT (VARCHAR, @ResourceTypeId) + ' AND ResourceTypeId < ' + CONVERT (VARCHAR, @ResourceTypeId) + ' + 1)';
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Add check', @Text = @Txt;
            SET @Txt = 'ALTER TABLE dbo.' + @Tbl + ' SWITCH PARTITION $partition.PartitionFunction_ResourceTypeId(' + CONVERT (VARCHAR, @ResourceTypeId) + ') TO dbo.' + @TblInt;
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch out start', @Text = @Txt;
            EXECUTE (@Txt);
            EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = @Tbl, @Action = 'Switch out end', @Text = @Txt;
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


GO
CREATE OR ALTER PROCEDURE [dbo].[SwitchPartitionsOutAllTables]
@RebuildClustered BIT
WITH EXECUTE AS SELF
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

GO
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IndexProperties')
BEGIN
    CREATE TABLE dbo.IndexProperties
    (
     IndexTableName     varchar(100)     NOT NULL
    ,IndexName     varchar(200)     NOT NULL
    ,PropertyName  varchar(100)     NOT NULL
    ,PropertyValue varchar(100)     NOT NULL
    ,CreateDate    datetime         NOT NULL CONSTRAINT DF_IndexProperties_CreateDate DEFAULT getUTCdate()
    
     CONSTRAINT PKC_IndexProperties_TableName_IndexName_PropertyName PRIMARY KEY CLUSTERED (IndexTableName, IndexName, PropertyName)
    )
    ON [PRIMARY]
END
GO

GO
CREATE OR ALTER PROCEDURE [dbo].[InitializeIndexProperties]
AS
SET NOCOUNT ON;
INSERT INTO dbo.IndexProperties (IndexTableName, IndexName, PropertyName, PropertyValue)
SELECT Tbl,
       Ind,
       'DATA_COMPRESSION',
       isnull(data_comp, 'NONE')
FROM   (SELECT O.Name AS Tbl,
               I.Name AS Ind,
               (SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END
                FROM   sys.partitions AS P
                WHERE  P.object_id = I.object_id
                       AND I.index_id = P.index_id) AS data_comp
        FROM   sys.indexes AS I
               INNER JOIN
               sys.objects AS O
               ON O.object_id = I.object_id
        WHERE  O.type = 'u'
               AND EXISTS (SELECT *
                           FROM   sys.partition_schemes AS PS
                           WHERE  PS.data_space_id = I.data_space_id
                                  AND name = 'PartitionScheme_ResourceTypeId')) AS A
WHERE  NOT EXISTS (SELECT *
                   FROM   dbo.IndexProperties
                   WHERE  IndexTableName = Tbl
                          AND IndexName = Ind);


GO
CREATE OR ALTER PROCEDURE dbo.EnqueueJobs @QueueType tinyint, @Definitions StringList READONLY, @GroupId bigint = NULL, @ForceOneActiveJobGroup bit, @IsCompleted bit = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'EnqueueJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' D='+convert(varchar,(SELECT count(*) FROM @Definitions))
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
                           +' F='+isnull(convert(varchar,@ForceOneActiveJobGroup),'NULL')
                           +' C='+isnull(convert(varchar,@IsCompleted),'NULL')
       ,@st datetime = getUTCdate()
       ,@Lock varchar(100) = 'EnqueueJobs_'+convert(varchar,@QueueType)
       ,@MaxJobId bigint
       ,@Rows int
       ,@msg varchar(1000)
       ,@JobIds BigintList
       ,@InputRows int

BEGIN TRY
  DECLARE @Input TABLE (DefinitionHash varbinary(20) PRIMARY KEY, Definition varchar(max))
  INSERT INTO @Input SELECT DefinitionHash = hashbytes('SHA1',String), Definition = String FROM @Definitions
  SET @InputRows = @@rowcount

  INSERT INTO @JobIds
    SELECT JobId
      FROM @Input A
           JOIN dbo.JobQueue B ON B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5
  
  IF @@rowcount < @InputRows
  BEGIN
    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    IF @ForceOneActiveJobGroup = 1 AND EXISTS (SELECT * FROM dbo.JobQueue WHERE QueueType = @QueueType AND Status IN (0,1) AND (@GroupId IS NULL OR GroupId <> @GroupId))
      RAISERROR('There are other active job groups',18,127)

    SET @MaxJobId = isnull((SELECT TOP 1 JobId FROM dbo.JobQueue WHERE QueueType = @QueueType ORDER BY JobId DESC),0)
  
    INSERT INTO dbo.JobQueue
        (
             QueueType
            ,GroupId
            ,JobId
            ,Definition
            ,DefinitionHash
            ,Status
        )
      OUTPUT inserted.JobId INTO @JobIds
      SELECT @QueueType
            ,GroupId = isnull(@GroupId,@MaxJobId+1)
            ,JobId
            ,Definition
            ,DefinitionHash
            ,Status = CASE WHEN @IsCompleted = 1 THEN 2 ELSE 0 END
        FROM (SELECT JobId = @MaxJobId + row_number() OVER (ORDER BY Dummy), * FROM (SELECT *, Dummy = 0 FROM @Input) A) A -- preserve input order
        WHERE NOT EXISTS (SELECT * FROM dbo.JobQueue B WHERE B.QueueType = @QueueType AND B.DefinitionHash = A.DefinitionHash AND B.Status <> 5)
    SET @Rows = @@rowcount

    COMMIT TRANSACTION
  END

  EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @Definitions StringList
--INSERT INTO @Definitions SELECT 'Test'
--EXECUTE dbo.EnqueueJobs 2, @Definitions, @ForceOneActiveJobGroup = 1

IF EXISTS(SELECT * FROM sys.objects WHERE name='EventLog') ALTER TABLE eventlog ALTER COLUMN Mode varchar(200) null;