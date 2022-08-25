GO
CREATE OR ALTER   PROCEDURE [dbo].[SwitchPartitionsOut]
@Tbl VARCHAR (100), @RebuildClustered BIT
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'SwitchPartitionsOut', @Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' ND=' + isnull(CONVERT (VARCHAR, @RebuildClustered), 'NULL'), @st AS DATETIME = getUTCdate(), @ResourceTypeId AS SMALLINT, @Rows AS BIGINT, @Txt AS VARCHAR (MAX), @TblInt AS VARCHAR (100), @IndId AS INT, @Ind AS VARCHAR (200), @Name AS VARCHAR (100), @checkName AS VARCHAR (200), @colName AS VARCHAR (200), @definition AS VARCHAR (200);
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
                    SELECT CHK.name AS CheckName,
                           CHK.definition AS CheckDefinition
                    FROM   sys.check_constraints AS CHK
                    WHERE  CHK.parent_object_id = object_id(@Tbl);
                    WHILE EXISTS (SELECT *
                                  FROM   @CheckConstraints)
                        BEGIN
                            SELECT TOP 1 @checkName = CheckName,
                                         @definition = CheckDefinition
                            FROM   @CheckConstraints;
                            SET @Txt = 'ALTER TABLE ' + @TblInt + ' ADD CONSTRAINT ' + @checkName + '_' + CONVERT (VARCHAR, @ResourceTypeId) + ' CHECK ' + @definition;
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
