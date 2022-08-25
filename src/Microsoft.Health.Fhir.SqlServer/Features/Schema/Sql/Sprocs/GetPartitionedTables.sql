GO
CREATE OR ALTER   PROCEDURE [dbo].[GetPartitionedTables]
@IncludeNotDisabled BIT, @IncludeNotSupported BIT
WITH EXECUTE AS 'dbo'
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

