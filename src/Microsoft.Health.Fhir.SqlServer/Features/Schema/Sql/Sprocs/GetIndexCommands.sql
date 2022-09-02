--IF object_id('GetIndexCommands') IS NOT NULL DROP PROCEDURE dbo.GetIndexCommands
CREATE   PROCEDURE [dbo].[GetIndexCommands]
@Tbl VARCHAR (100), @Ind VARCHAR (200), @AddPartClause BIT, @IncludeClustered BIT, @Txt VARCHAR (MAX)=NULL OUTPUT
WITH EXECUTE AS SELF
AS
SET NOCOUNT ON;
DECLARE @SP AS VARCHAR (100) = 'GetIndexCommands'
       ,@Mode AS VARCHAR (200) = 'Tbl=' + isnull(@Tbl, 'NULL') + ' Ind=' + isnull(@Ind, 'NULL')
       ,@st AS DATETIME = getUTCdate()
       ,@DataComp AS VARCHAR (100)
       ,@FilterDef AS VARCHAR (200)
       ,@CommandForKey AS VARCHAR (200)
       ,@CommandForInc AS VARCHAR (200)
       ,@TblId AS INT
       ,@IndId AS INT
       ,@colname AS VARCHAR (100)
       ,@PartClause AS VARCHAR (100);

DECLARE @KeyColsTable TABLE (KeyCol VARCHAR (200));
DECLARE @IncColsTable TABLE (IncCol VARCHAR (200));
DECLARE @Table_index TABLE (object_id INT, index_id  INT);
DECLARE @Indexes TABLE (Ind VARCHAR (200) PRIMARY KEY, Txt VARCHAR (MAX));
DECLARE @Temp TABLE (object_id INT, index_id INT, KeyCols VARCHAR (200), IncCols VARCHAR (200));

BEGIN TRY
  IF @Tbl IS NULL RAISERROR ('@Tbl IS NULL', 18, 127);

  INSERT INTO @Table_index
    SELECT I.object_id,
           I.index_id
    FROM   sys.indexes AS I JOIN sys.objects AS O ON I.object_id = O.object_id
    WHERE  O.name = @Tbl
      AND I.name = @Ind;
  WHILE EXISTS (SELECT * FROM   @Table_index)
    BEGIN
      SELECT TOP 1 @TblId = object_id, @IndId = index_id
      FROM   @Table_index;
      SET @CommandForKey = '';
      SET @CommandForInc = '';
      DELETE @KeyColsTable;
      INSERT INTO @KeyColsTable
        SELECT C.name
        FROM sys.index_columns AS IC JOIN sys.indexes AS I ON IC.object_id = I.object_id AND IC.index_id = I.index_id, sys.columns AS C
        WHERE C.column_id = IC.column_id
          AND C.object_id = IC.object_id
          AND IC.object_id = @TblId
          AND IC.index_id = @IndId
          AND IC.key_ordinal > 0
          AND IC.is_included_column = 0
        ORDER BY key_ordinal;
      WHILE EXISTS (SELECT * FROM @KeyColsTable)
        BEGIN
          SELECT TOP 1 @colname = KeyCol
          FROM   @KeyColsTable;
          SET @CommandForKey = @CommandForKey + @colname + ',';
          DELETE @KeyColsTable WHERE  KeyCol = @colname;
        END
      DELETE @IncColsTable;
      INSERT INTO @IncColsTable
        SELECT   C.name
        FROM sys.index_columns AS IC JOIN sys.indexes AS I ON IC.object_id = I.object_id AND IC.index_id = I.index_id, sys.columns AS C
        WHERE C.column_id = IC.column_id
          AND C.object_id = IC.object_id
          AND IC.object_id = @TblId
          AND IC.index_id = @IndId
          AND IC.is_included_column = 1
        ORDER BY key_ordinal;
      WHILE EXISTS (SELECT * FROM @IncColsTable)
          BEGIN
            SELECT TOP 1 @colname = IncCol FROM @IncColsTable;
            SET @CommandForInc = @CommandForInc + @colname + ',';
            DELETE @IncColsTable WHERE  IncCol = @colname;
          END
      SET @DataComp = isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions AS P WHERE  P.object_id = @TblId AND P.index_id = @IndId)
                            ,(SELECT TOP 1 NULLIF (PropertyValue, 'NONE') FROM dbo.IndexProperties, sys.objects AS O, sys.indexes AS I
                              WHERE  IndexTableName = O.Name
                                AND IndexName = I.Name
                                AND O.name = @Tbl
                                AND I.name = @Ind
                                AND PropertyName = 'DATA_COMPRESSION'));
      SELECT @FilterDef = replace(replace(replace(replace(I.filter_definition, '[', ''), ']', ''), '(', ''), ')', '')
      FROM sys.indexes AS I
      WHERE I.object_id = @TblId AND I.index_id = @IndId;
      SELECT @PartClause = CASE WHEN EXISTS (SELECT * FROM sys.partition_schemes AS S, sys.indexes AS I 
                                             WHERE S.data_space_id = I.data_space_id
                                               AND S.name = 'PartitionScheme_ResourceTypeId'
                                               AND I.object_id = @TblId
                                               AND I.name = @Ind) THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END;
      INSERT INTO @Indexes
        SELECT @Ind AS Ind
              ,CASE
                 WHEN is_primary_key = 1 
                   THEN 'ALTER TABLE dbo.[' + @Tbl + '] ADD PRIMARY KEY ' + CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END -- Skip PK name, then this string can be applied to all component tables with no changes.
                 ELSE 'CREATE' + CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END + CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END + ' INDEX ' + @Ind + ' ON dbo.[' + @Tbl + ']' 
               END 
              +' (' + LEFT(@CommandForKey, len(@CommandForKey) - 1) + ')' 
              +CASE WHEN @CommandForInc <> '' THEN ' INCLUDE (' + LEFT(@CommandForInc, len(@CommandForInc) - 1) + ')' ELSE '' END
              +CASE WHEN @FilterDef IS NOT NULL THEN ' WHERE ' + @FilterDef ELSE '' END + CASE WHEN @DataComp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = ' + @DataComp + ')' ELSE '' END 
              +CASE WHEN @AddPartClause = 1 THEN @PartClause ELSE '' END AS Txt
        FROM sys.indexes AS I JOIN sys.objects AS O ON I.object_id = O.object_id
        WHERE O.object_id = @TblId
          AND I.index_id = @IndId
          AND (@IncludeClustered = 1 OR index_id > 1);
      DELETE @Table_index WHERE  object_id = @TblId;
    END
  EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Info', @Target = '@Indexes', @Action = 'Insert', @Rows = @@rowcount;
  IF @Ind IS NULL -- return records
    SELECT Ind, Txt FROM @Indexes
  ELSE
    SET @Txt = (SELECT Txt FROM @Indexes) -- There should be only one record

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Text=@Txt
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO

