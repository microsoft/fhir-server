--IF object_id('SwitchPartitionsOutAllTables') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsOutAllTables
GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsOutAllTables @RebuildClustered bit
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsOutAllTables'
       ,@Mode varchar(200) = 'PS=PartitionScheme_ResourceTypeId ND='+isnull(convert(varchar,@RebuildClustered),'NULL')
       ,@st datetime = getUTCdate()
       ,@Tbl varchar(100)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Tables TABLE (name varchar(100) PRIMARY KEY, supported bit)
  INSERT INTO @Tables EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = @RebuildClustered, @IncludeNotSupported = 0
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Tables',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Tables)
  BEGIN
    SET @Tbl = (SELECT TOP 1 name FROM @Tables ORDER BY name)

    EXECUTE dbo.SwitchPartitionsOut @Tbl = @Tbl, @RebuildClustered = @RebuildClustered
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='SwitchPartitionsOut',@Action='Execute',@Text=@Tbl

    DELETE FROM @Tables WHERE name = @Tbl
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--IF object_id('SwitchPartitionsOut') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsOut
GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsOut @Tbl varchar(100), @RebuildClustered bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsOut'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' ND='+isnull(convert(varchar,@RebuildClustered),'NULL')
       ,@st datetime = getUTCdate()
       ,@ResourceTypeId smallint
       ,@Rows bigint
       ,@Txt varchar(max)
       ,@TblInt varchar(100)
       ,@IndId int
       ,@Ind varchar(200)
       ,@Name varchar(100)
       ,@checkName varchar(200)
       ,@definition varchar(200)

DECLARE @Indexes TABLE (IndId int PRIMARY KEY, name varchar(200), IsDisabled bit)
DECLARE @IndexesRT TABLE (IndId int PRIMARY KEY, name varchar(200), IsDisabled bit)
DECLARE @ResourceTypes TABLE (ResourceTypeId smallint PRIMARY KEY, partition_number_roundtrip int, partition_number int, row_count bigint)
DECLARE @Names TABLE (name varchar(100) PRIMARY KEY)
DECLARE @CheckConstraints TABLE (CheckName varchar(200), CheckDefinition varchar(200))

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL', 18, 127)
  IF @RebuildClustered IS NULL RAISERROR('@RebuildClustered IS NULL', 18, 127)

  INSERT INTO @Indexes SELECT index_id, name, is_disabled FROM sys.indexes WHERE object_id = object_id(@Tbl) AND (is_disabled = 0 OR @RebuildClustered = 1)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  INSERT INTO @ResourceTypes 
    SELECT ResourceTypeId = partition_number - 1
          ,partition_number_roundtrip = $partition.PartitionFunction_ResourceTypeId(partition_number - 1)
          ,partition_number
          ,row_count
      FROM sys.dm_db_partition_stats 
      WHERE object_id = object_id(@Tbl) 
        AND index_id = 1
        AND row_count > 0
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@ResourceTypes',@Action='Insert',@Rows=@@rowcount,@Text='For partition switch'

  -- Sanity check
  IF EXISTS (SELECT * FROM @ResourceTypes WHERE partition_number_roundtrip <> partition_number) RAISERROR('Partition sanity check failed', 18, 127)

  WHILE EXISTS (SELECT * FROM @ResourceTypes)
  BEGIN
    SELECT TOP 1 @ResourceTypeId = ResourceTypeId, @Rows = row_count FROM @ResourceTypes ORDER BY ResourceTypeId
    SET @TblInt = @Tbl+'_'+convert(varchar,@ResourceTypeId)
    SET @Txt = 'Starting @ResourceTypeId='+convert(varchar,@ResourceTypeId)+' row_count='+convert(varchar,@Rows)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Text=@Txt

    IF NOT EXISTS (SELECT * FROM sysindexes WHERE id = object_id(@TblInt) AND rows > 0)
    BEGIN
      IF object_id(@TblInt) IS NOT NULL
      BEGIN
        EXECUTE('DROP TABLE dbo.'+@TblInt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Drop'
      END

      EXECUTE('SELECT * INTO dbo.'+@TblInt+' FROM dbo.'+@Tbl+' WHERE 1 = 2')
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Select Into',@Rows=@@rowcount

      DELETE FROM @CheckConstraints     
      INSERT INTO @CheckConstraints SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = object_id(@Tbl) 
      WHILE EXISTS (SELECT * FROM @CheckConstraints)
      BEGIN
        SELECT TOP 1 @checkName=CheckName, @definition=CheckDefinition from @CheckConstraints
        SET @Txt = 'ALTER TABLE '+@TblInt+' ADD CHECK '+@definition
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='ALTER',@Text=@Txt

        DELETE FROM @CheckConstraints WHERE CheckName=@checkName
      END

      DELETE FROM @Names
      INSERT INTO @Names SELECT name FROM sys.columns WHERE object_id = object_id(@Tbl) AND is_sparse = 1
      WHILE EXISTS (SELECT * FROM @Names) 
      BEGIN
        SET @Name = (SELECT TOP 1 name FROM @Names ORDER BY name)

        -- This is not generic but works for old and current schema
        SET @Txt = (SELECT 'ALTER TABLE dbo.'+@TblInt+' ALTER COLUMN '+@Name+' '+T.name+'('+convert(varchar,C.precision)+','+convert(varchar,C.scale)+') SPARSE NULL'
                      FROM sys.types T JOIN sys.columns C ON C.system_type_id = T.system_type_id
                      WHERE C.object_id = object_id(@Tbl)
                        AND C.name = @Name
                   )
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='ALTER',@Text=@Txt

        DELETE FROM @Names WHERE name = @Name
      END
    END

    -- Create all indexes/pks, exclude disabled 
    INSERT INTO @IndexesRT SELECT * FROM @Indexes WHERE IsDisabled = 0
    WHILE EXISTS (SELECT * FROM @IndexesRT)
    BEGIN
      SELECT TOP 1 @IndId = IndId, @Ind = name FROM @IndexesRT ORDER BY IndId

      IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(@TblInt) AND name = @Ind)
      BEGIN 
        EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 1, @Txt = @Txt OUT

        SET @Txt = replace(@Txt,'['+@Tbl+']',@TblInt)
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Create Index',@Text=@Txt
      END

      DELETE FROM @IndexesRT WHERE IndId = @IndId
    END

    SET @Txt = 'ALTER TABLE dbo.'+@TblInt+' ADD CHECK (ResourceTypeId >= '+convert(varchar,@ResourceTypeId)+' AND ResourceTypeId < '+convert(varchar,@ResourceTypeId)+' + 1)' -- this matches partition function
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Add check',@Text=@Txt

    -- Switch out
    SET @Txt = 'ALTER TABLE dbo.'+@Tbl+' SWITCH PARTITION $partition.PartitionFunction_ResourceTypeId('+convert(varchar,@ResourceTypeId)+') TO dbo.'+@TblInt
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Switch out start',@Text=@Txt
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Switch out end',@Text=@Txt

    DELETE FROM @ResourceTypes WHERE ResourceTypeId = @ResourceTypeId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--IF object_id('SwitchPartitionsInAllTables') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsInAllTables
GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsInAllTables
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsInAllTables'
       ,@Mode varchar(200) = 'PS=PartitionScheme_ResourceTypeId'
       ,@st datetime = getUTCdate()
       ,@Tbl varchar(100)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Tables TABLE (name varchar(100) PRIMARY KEY, supported bit)
  INSERT INTO @Tables EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Tables',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Tables)
  BEGIN
    SET @Tbl = (SELECT TOP 1 name FROM @Tables ORDER BY name)

    EXECUTE dbo.SwitchPartitionsIn @Tbl = @Tbl
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='SwitchPartitionsIn',@Action='Execute',@Text=@Tbl

    DELETE FROM @Tables WHERE name = @Tbl
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--IF object_id('SwitchPartitionsIn') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsIn
GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsIn @Tbl varchar(100)
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsIn'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')
       ,@st datetime = getUTCdate()
       ,@ResourceTypeId smallint
       ,@Rows bigint
       ,@Txt varchar(1000)
       ,@TblInt varchar(100)
       ,@Ind varchar(200)
       ,@IndId int
       ,@DataComp varchar(100)

DECLARE @Indexes TABLE (IndId int PRIMARY KEY, name varchar(200))
DECLARE @ResourceTypes TABLE (ResourceTypeId smallint PRIMARY KEY)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL', 18, 127)

  -- Partitioned table should be either empty, then rebuilt indexes, or all indexes are already built
  INSERT INTO @Indexes SELECT index_id, name FROM sys.indexes WHERE object_id = object_id(@Tbl) AND is_disabled = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Indexes)
  BEGIN
    SELECT TOP 1 @IndId = IndId, @Ind = name FROM @Indexes ORDER BY IndId

    SET @DataComp = CASE WHEN (SELECT PropertyValue FROM dbo.IndexProperties WHERE TableName=@Tbl AND IndexName=@Ind)='PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END
    SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('''+@Tbl+''') AND name = '''+@Ind+''' AND is_disabled = 1) ALTER INDEX '+@Ind+' ON dbo.'+@Tbl+' REBUILD'+@DataComp
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Ind,@Action='Rebuild',@Text=@Txt

    DELETE FROM @Indexes WHERE IndId = @IndId
  END

  INSERT INTO @ResourceTypes
    SELECT ResourceTypeId = convert(smallint,substring(name,charindex('_',name)+1,6))
      FROM sys.objects O
      WHERE name LIKE @Tbl+'[_]%'
        AND EXISTS (SELECT * FROM sysindexes WHERE id = O.object_id AND indid IN (0,1) AND rows > 0)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='#ResourceTypes',@Action='Select Into',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @ResourceTypes)
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @ResourceTypes)
    SET @TblInt = @Tbl+'_'+convert(varchar,@ResourceTypeId)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt

    SET @Txt = 'ALTER TABLE dbo.'+@TblInt+' SWITCH TO dbo.'+@Tbl+' PARTITION $partition.PartitionFunction_ResourceTypeId('+convert(varchar,@ResourceTypeId)+')'
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Switch in start',@Text=@Txt
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Tbl,@Action='Switch in',@Text=@Txt

    -- Sanity check
    IF EXISTS (SELECT * FROM sysindexes WHERE id = object_id(@TblInt) AND rows > 0)
    BEGIN
      SET @Txt = @TblInt+' is not empty after switch'
      RAISERROR(@Txt, 18, 127)
    END

    EXECUTE('DROP TABLE dbo.'+@TblInt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Drop'

    DELETE FROM @ResourceTypes WHERE ResourceTypeId = @ResourceTypeId
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--DROP PROCEDURE dbo.InitDefrag
GO
CREATE OR ALTER PROCEDURE dbo.InitDefrag @QueueType tinyint, @GroupId bigint, @DefragItems int = NULL OUT
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'InitDefrag'
       ,@st datetime = getUTCdate()
       ,@ObjectId int
       ,@msg varchar(1000)
       ,@Rows int
       ,@MinFragPct int = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.MinFragPct'),10)
       ,@MinSizeGB float = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.MinSizeGB'),0.1)
       ,@DefinitionsSorted StringList

DECLARE @Mode varchar(200) = 'G='+convert(varchar,@GroupId)+' MF='+convert(varchar,@MinFragPct)+' MS='+convert(varchar,@MinSizeGB)
-- !!! Make sure that only one thread runs this logic

DECLARE @Definitions AS TABLE (Def varchar(900) PRIMARY KEY, FragGB float)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  SELECT *
    INTO #filter
    FROM (SELECT object_id
                ,ReservedGB = sum(reserved_page_count*8.0/1024/1024)
            FROM sys.dm_db_partition_stats A
            WHERE object_id IN (SELECT object_id FROM sys.objects WHERE type = 'U' AND name NOT IN ('EventLog'))
            GROUP BY
                object_id
        ) A
    WHERE ReservedGB > @MinSizeGB

  WHILE EXISTS (SELECT * FROM #filter) -- no indexes
  BEGIN
    SET @ObjectId = (SELECT TOP 1 object_id FROM #filter ORDER BY ReservedGB DESC)

    INSERT INTO @Definitions
      SELECT object_name(@ObjectId)
            +';'+I.name
            +';'+convert(varchar,partition_number)
            +';'+convert(varchar,CASE WHEN EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id) THEN 1 ELSE 0 END)
            +';'+convert(varchar,(SELECT sum(reserved_page_count) FROM sys.dm_db_partition_stats S WHERE S.object_id = A.object_id AND S.index_id = A.index_id AND S.partition_number = A.partition_number)*8.0/1024/1024)
            ,FragGB
        FROM (SELECT object_id, index_id, partition_number, FragGB = A.avg_fragmentation_in_percent*A.page_count*8.0/1024/1024/100
                FROM sys.dm_db_index_physical_stats(db_id(), @ObjectId, NULL, NULL, 'LIMITED') A
                WHERE index_id > 0
                  AND avg_fragmentation_in_percent >= @MinFragPct AND A.page_count > 500
             ) A
             JOIN sys.indexes I ON I.object_id = A.object_id AND I.index_id = A.index_id
    SET @Rows = @@rowcount
    SET @msg = object_name(@ObjectId)
    EXECUTE dbo.LogEvent @Process=@SP,@Status='Run',@Mode=@Mode,@Target='@Definitions',@Action='Insert',@Rows=@Rows,@Text=@msg

    DELETE FROM #filter WHERE object_id = @ObjectId
  END

  INSERT INTO @DefinitionsSorted SELECT Def+';'+convert(varchar,FragGB) FROM @Definitions ORDER BY FragGB DESC
  SET @DefragItems = @@rowcount

  IF @DefragItems > 0
    EXECUTE dbo.EnqueueJobs @QueueType = @QueueType, @Definitions = @DefinitionsSorted, @GroupId = @GroupId, @ForceOneActiveJobGroup = 1

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--IF object_id('GetPartitionedTables') IS NOT NULL DROP PROCEDURE dbo.GetPartitionedTables
GO
CREATE OR ALTER PROCEDURE dbo.GetPartitionedTables @IncludeNotDisabled bit, @IncludeNotSupported bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetPartitionedTables'
       ,@Mode varchar(200) = 'PS=PartitionScheme_ResourceTypeId D='+isnull(convert(varchar,@IncludeNotDisabled),'NULL')+' S='+isnull(convert(varchar,@IncludeNotSupported),'NULL')
       ,@st datetime = getUTCdate()

DECLARE @NotSupportedTables TABLE (id int PRIMARY KEY)

BEGIN TRY
  INSERT INTO @NotSupportedTables
    SELECT DISTINCT O.object_id
      FROM sys.indexes I
           JOIN sys.objects O ON O.object_id = I.object_id
      WHERE O.type = 'u'
        AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
        -- table is supported if all indexes contain ResourceTypeId as key column and all indexes are partitioned on the same scheme
        AND (NOT EXISTS 
               (SELECT * 
                  FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id 
                  WHERE IC.object_id = I.object_id
                    AND IC.index_id = I.index_id
                    AND IC.key_ordinal > 0
                    AND IC.is_included_column = 0 
                    AND C.name = 'ResourceTypeId'
               )
             OR 
             EXISTS 
               (SELECT * 
                  FROM sys.indexes NSI 
                  WHERE NSI.object_id = O.object_id 
                    AND NOT EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = NSI.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
               )
            )

  SELECT convert(varchar(100),O.name), convert(bit,CASE WHEN EXISTS (SELECT * FROM @NotSupportedTables NSI WHERE NSI.id = O.object_id) THEN 0 ELSE 1 END)
    FROM sys.indexes I
         JOIN sys.objects O ON O.object_id = I.object_id
    WHERE O.type = 'u'
      AND I.index_id IN (0,1)
      AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
      AND EXISTS (SELECT * FROM sys.index_columns IC JOIN sys.columns C ON C.object_id = I.object_id AND C.column_id = IC.column_id AND IC.is_included_column = 0 AND C.name = 'ResourceTypeId')
      AND (@IncludeNotSupported = 1 
           OR NOT EXISTS (SELECT * FROM @NotSupportedTables NSI WHERE NSI.id = O.object_id)
          )
      AND (@IncludeNotDisabled = 1 OR EXISTS (SELECT * FROM sys.indexes D WHERE D.object_id = O.object_id AND D.is_disabled = 1))
    ORDER BY 1

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--IF object_id('GetIndexCommands') IS NOT NULL DROP PROCEDURE dbo.GetIndexCommands
GO
CREATE OR ALTER PROCEDURE dbo.GetIndexCommands @Tbl varchar(100), @Ind varchar(200), @AddPartClause bit, @IncludeClustered bit, @Txt varchar(max) = NULL OUT
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetIndexCommands'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' Ind='+isnull(@Ind,'NULL')
       ,@st datetime = getUTCdate()

DECLARE @Indexes TABLE (Ind varchar(200) PRIMARY KEY, Txt varchar(max))

BEGIN TRY
  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)

  INSERT INTO @Indexes
    SELECT Ind
          ,CASE 
             WHEN is_primary_key = 1 
               THEN 'ALTER TABLE dbo.['+Tbl+'] ADD PRIMARY KEY '+CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END -- Skip PK name, then this string can be applied to all component tables with no changes.
             ELSE 'CREATE'+CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END+CASE WHEN type = 1 THEN ' CLUSTERED' ELSE '' END+' INDEX '+Ind+' ON dbo.['+Tbl+']'
           END
          +' ('+KeyCols+')'
          +IncClause
          +CASE WHEN filter_def IS NOT NULL THEN ' WHERE '+filter_def ELSE '' END
          +CASE WHEN data_comp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = '+data_comp+')' ELSE '' END
          +CASE WHEN @AddPartClause = 1 THEN PartClause ELSE '' END
      FROM (SELECT Tbl = O.Name
                  ,Ind = I.Name
                  ,data_comp = isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = I.object_id AND I.index_id = P.index_id)
                                     ,(SELECT nullif(PropertyValue,'NONE') FROM dbo.IndexProperties WHERE TableName = O.Name AND IndexName = I.Name AND PropertyName = 'DATA_COMPRESSION')
                                     )
                  ,filter_def = replace(replace(replace(replace(I.filter_definition,'[',''),']',''),'(',''),')','')
                  ,I.is_unique
                  ,I.is_primary_key
                  ,I.type
                  ,KeyCols
                  ,IncClause = CASE WHEN IncCols IS NOT NULL THEN ' INCLUDE ('+IncCols+')' ELSE '' END
                  ,PartClause = CASE WHEN EXISTS (SELECT * FROM sys.partition_schemes S WHERE S.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId') THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END
              FROM sys.indexes I
                   JOIN sys.objects O ON O.object_id = I.object_id
                   CROSS APPLY (SELECT KeyCols = string_agg(CASE WHEN IC.key_ordinal > 0 AND IC.is_included_column = 0 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal)
                                      ,IncCols = string_agg(CASE WHEN IC.is_included_column = 1 THEN C.name END, ',') WITHIN GROUP (ORDER BY key_ordinal)
                                  FROM sys.index_columns IC
                                       JOIN sys.columns C ON C.object_id = IC.object_id AND C.column_id = IC.column_id
                                  WHERE IC.object_id = I.object_id AND IC.index_id = I.index_id
                                  GROUP BY 
                                       IC.object_id
                                      ,IC.index_id
                               ) IC
              WHERE O.name = @Tbl
                AND (@Ind IS NULL OR I.name = @Ind)
                AND (@IncludeClustered = 1 OR index_id > 1)
           ) A
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

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
--IF object_id('GetCommandsForRebuildIndexes') IS NOT NULL DROP PROCEDURE dbo.GetCommandsForRebuildIndexes
GO
CREATE OR ALTER PROCEDURE dbo.GetCommandsForRebuildIndexes @RebuildClustered bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'GetCommandsForRebuildIndexes'
       ,@Mode varchar(200) = 'PS=PartitionScheme_ResourceTypeId RC='+isnull(convert(varchar,@RebuildClustered),'NULL')
       ,@st datetime = getUTCdate()
       ,@Tbl varchar(100)
       ,@TblInt varchar(100)
       ,@Ind varchar(200)
       ,@IndId int
       ,@Supported bit
       ,@Txt varchar(max)
       ,@Rows bigint
       ,@Pages bigint
       ,@ResourceTypeId smallint
       ,@IndexesCnt int
       ,@DataComp varchar(100)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Commands TABLE (Tbl varchar(100), Ind varchar(200), Txt varchar(max), Pages bigint)
  DECLARE @ResourceTypes TABLE (ResourceTypeId smallint PRIMARY KEY)
  DECLARE @Indexes TABLE (Ind varchar(200) PRIMARY KEY, IndId int)
  DECLARE @Tables TABLE (name varchar(100) PRIMARY KEY, Supported bit)

  INSERT INTO @Tables EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Tables',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Tables)
  BEGIN
    SELECT TOP 1 @Tbl = name, @Supported = Supported FROM @Tables ORDER BY name

    IF @Supported = 0
    BEGIN
      INSERT INTO @Commands
        SELECT @Tbl, name, 'ALTER INDEX '+name+' ON dbo.'+@Tbl+' REBUILD'+CASE WHEN (SELECT PropertyValue from dbo.IndexProperties where TableName=@Tbl AND IndexName=name)='PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END, convert(bigint,9e18) FROM sys.indexes WHERE object_id = object_id(@Tbl) AND (is_disabled = 1 AND index_id > 1 AND @RebuildClustered = 0 OR index_id = 1 AND @RebuildClustered = 1)
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Commands',@Action='Insert',@Rows=@@rowcount,@Text='Not supported tables with disabled indexes'
    END
    ELSE
    BEGIN
      DELETE FROM @ResourceTypes
      INSERT INTO @ResourceTypes 
        SELECT ResourceTypeId = convert(smallint,substring(name,charindex('_',name)+1,6))
          FROM sys.sysobjects 
          WHERE name LIKE @Tbl+'[_]%'
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@ResourceTypes',@Action='Insert',@Rows=@@rowcount

      WHILE EXISTS (SELECT * FROM @ResourceTypes)
      BEGIN
        SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @ResourceTypes ORDER BY ResourceTypeId)
        SET @TblInt = @Tbl+'_'+convert(varchar,@ResourceTypeId)
        SET @Pages = (SELECT dpages FROM sysindexes WHERE id = object_id(@TblInt) AND indid IN (0,1)) 

        -- add indexes
        DELETE FROM @Indexes
        INSERT INTO @Indexes SELECT name, index_id FROM sys.indexes WHERE object_id = object_id(@Tbl) AND (index_id > 1 AND @RebuildClustered = 0 OR index_id = 1 AND @RebuildClustered = 1) -- indexes in target table
        SET @IndexesCnt = 0
        WHILE EXISTS (SELECT * FROM @Indexes)
        BEGIN
          SELECT TOP 1 @Ind = Ind, @IndId = IndId FROM @Indexes ORDER BY Ind

          IF @IndId = 1
          BEGIN
            SET @Txt = 'ALTER INDEX '+@Ind+' ON dbo.'+@TblInt+' REBUILD'+CASE WHEN (SELECT PropertyValue from dbo.IndexProperties where TableName=@Tbl and IndexName=@Ind)='PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END
            INSERT INTO @Commands SELECT @TblInt, @Ind, @Txt, @Pages
            EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Add command',@Rows=@@rowcount,@Text=@Txt
          END
          ELSE
            IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id(@TblInt) AND name = @Ind) -- not existing indexes in source table
            BEGIN
              EXECUTE dbo.GetIndexCommands @Tbl = @Tbl, @Ind = @Ind, @AddPartClause = 0, @IncludeClustered = 0, @Txt = @Txt OUT
              SET @Txt = replace(@Txt,'['+@Tbl+']',@TblInt)
              IF @Txt IS NOT NULL
              BEGIN
                SET @IndexesCnt = @IndexesCnt + 1
                INSERT INTO @Commands SELECT @TblInt, @Ind, @Txt, @Pages
                EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Add command',@Rows=@@rowcount,@Text=@Txt
              END
            END

          DELETE FROM @Indexes WHERE Ind = @Ind
        END

        IF @IndexesCnt > 1
        BEGIN
          -- add update stats so index creates are not waiting on each other
          INSERT INTO @Commands SELECT @TblInt, 'UPDATE STAT', 'UPDATE STATISTICS dbo.'+@TblInt, @Pages
          EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Add command',@Rows=@@rowcount,@Text='Add stats update'
        END

        DELETE FROM @ResourceTypes WHERE ResourceTypeId = @ResourceTypeId
      END
    END

    DELETE FROM @Tables WHERE name = @Tbl
  END

  SELECT Tbl, Ind, Txt FROM @Commands ORDER BY Pages DESC, Tbl, CASE WHEN Txt LIKE 'UPDATE STAT%' THEN 0 ELSE 1 END -- update stats should be before index creates
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Commands',@Action='Select',@Rows=@@rowcount

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--IF object_id('ExecuteCommandForRebuildIndexes') IS NOT NULL DROP PROCEDURE dbo.ExecuteCommandForRebuildIndexes
GO
CREATE OR ALTER PROCEDURE dbo.ExecuteCommandForRebuildIndexes @Tbl varchar(100), @Ind varchar(1000), @Cmd varchar(max)
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'ExecuteCommandForRebuildIndexes' 
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')
       ,@st datetime
       ,@Retries int = 0
       ,@Action varchar(100)
       ,@msg varchar(1000)

RetryOnTempdbError:

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@Cmd

  SET @st = getUTCdate()

  IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)
  IF @Cmd IS NULL RAISERROR('@Cmd IS NULL',18,127)

  SET @Action = CASE 
                  WHEN @Cmd LIKE 'UPDATE STAT%' THEN 'Update statistics'
                  WHEN @Cmd LIKE 'CREATE%INDEX%' THEN 'Create Index'
                  WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD%' THEN 'Rebuild Index'
                  WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint'
                END
  IF @Action IS NULL 
  BEGIN
    SET @msg = 'Not supported command = '+convert(varchar(900),@Cmd)
    RAISERROR(@msg,18,127)
  END

  IF @Action = 'Create Index' WAITFOR DELAY '00:00:05'

  EXECUTE(@Cmd)
  SELECT @Ind;

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Action=@Action,@Status='End',@Start=@st,@Text=@Cmd
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF error_number() = 40544 -- '%database ''tempdb'' has reached its size quota%'
  BEGIN
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st,@ReRaisError=0,@Retry=@Retries
    SET @Retries = @Retries + 1
    IF @Tbl = 'TokenText_96' 
      WAITFOR DELAY '01:00:00' -- 1 hour
    ELSE 
      WAITFOR DELAY '00:10:00' -- 10 minutes
    GOTO RetryOnTempdbError
  END
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.DisableIndex
    @tableName nvarchar(128),
    @indexName nvarchar(128)
WITH EXECUTE AS 'dbo'
AS
DECLARE @errorTxt as varchar(1000)
       ,@sql as nvarchar (1000)
       ,@isDisabled as bit

IF object_id(@tableName) IS NULL
BEGIN
    SET @errorTxt = @tableName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

SET @isDisabled = (SELECT is_disabled FROM sys.indexes WHERE object_id = object_id(@tableName) AND name = @indexName)
IF @isDisabled IS NULL
BEGIN
    SET @errorTxt = @indexName +' does not exist or you don''t have permissions.'
    RAISERROR(@errorTxt, 18, 127)
END

IF @isDisabled = 0
BEGIN
    SET @sql = N'ALTER INDEX ' + QUOTENAME(@indexName) + N' on ' + @tableName + ' Disable'
    EXECUTE sp_executesql @sql
END
GO
--DROP PROCEDURE dbo.DefragChangeDatabaseSettings
GO
CREATE OR ALTER PROCEDURE dbo.DefragChangeDatabaseSettings @IsOn bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'DefragChangeDatabaseSettings'
       ,@Mode varchar(200) = 'On='+convert(varchar,@IsOn)
       ,@st datetime = getUTCdate()
       ,@SQL varchar(3500) 

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Start',@Mode=@Mode

  SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_UPDATE_STATISTICS '+CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END
  EXECUTE(@SQL)
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Run',@Mode=@Mode,@Text=@SQL

  SET @SQL = 'ALTER DATABASE CURRENT SET AUTO_CREATE_STATISTICS '+CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END
  EXECUTE(@SQL)

  EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Mode=@Mode,@Start=@st,@Text=@SQL
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--EXECUTE dbo.DefragChangeDatabaseSettings 1
--DROP PROCEDURE dbo.Defrag
GO
CREATE OR ALTER PROCEDURE dbo.Defrag @TableName varchar(100), @IndexName varchar(200), @PartitionNumber int, @IsPartitioned bit
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'Defrag'
       ,@Mode varchar(200) = @TableName+'.'+@IndexName+'.'+convert(varchar,@PartitionNumber)+'.'+convert(varchar,@IsPartitioned)
       ,@st datetime = getUTCdate()
       ,@SQL varchar(3500) 
       ,@msg varchar(1000)
       ,@SizeBefore float
       ,@SizeAfter float
       ,@IndexId int

BEGIN TRY
  SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = object_id(@TableName) AND name = @IndexName)
  SET @SizeBefore = (SELECT sum(reserved_page_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id(@TableName) AND index_id = @IndexId) * 8.0 / 1024 / 1024
  SET @msg = 'Size[GB] before='+convert(varchar,@SizeBefore)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@msg

  SET @Sql = 'ALTER INDEX '+quotename(@IndexName)+' ON dbo.'+quotename(@TableName)+' REORGANIZE'+CASE WHEN @IsPartitioned = 1 THEN ' PARTITION = '+convert(varchar,@PartitionNumber) ELSE '' END

  BEGIN TRY
    EXECUTE(@Sql)
    SET @SizeAfter = (SELECT sum(reserved_page_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id(@TableName) AND index_id = @IndexId) * 8.0 / 1024 / 1024
    SET @msg = 'Size[GB] before='+convert(varchar,@SizeBefore)+', after='+convert(varchar,@SizeAfter)+', reduced by='+convert(varchar,@SizeBefore-@SizeAfter)
    EXECUTE dbo.LogEvent @Process=@SP,@Status='End',@Mode=@Mode,@Action='Reorganize',@Start=@st,@Text=@msg
  END TRY
  BEGIN CATCH
    EXECUTE dbo.LogEvent @Process=@SP,@Status='Error',@Mode=@Mode,@Action='Reorganize',@Start=@st,@ReRaisError=0
  END CATCH
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--INSERT INTO Parameters (Id,Char) SELECT 'Defrag','LogEvent'
--SELECT TOP 200 * FROM EventLog ORDER BY EventDate DESC
