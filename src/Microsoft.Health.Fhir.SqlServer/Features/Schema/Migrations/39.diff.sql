﻿--IF object_id('ExecuteCommandForRebuildIndexes') IS NOT NULL DROP PROCEDURE dbo.ExecuteCommandForRebuildIndexes
GO
CREATE OR ALTER PROCEDURE dbo.ExecuteCommandForRebuildIndexes @Tbl varchar(100), @Ind varchar(100), @Cmd varchar(max), @Pid int
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
                  WHEN @Cmd LIKE 'ALTER%INDEX%REBUILD' THEN 'Rebuild Index'
                  WHEN @Cmd LIKE 'ALTER%TABLE%ADD%' THEN 'Add Constraint'
                END
  IF @Action IS NULL 
  BEGIN
    SET @msg = 'Not supported command = '+convert(varchar(900),@Cmd)
    RAISERROR(@msg,18,127)
  END

  IF @Action = 'Create Index' WAITFOR DELAY '00:00:05'

  EXECUTE(@Cmd)
  select @Ind, @Pid
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

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Commands TABLE (Tbl varchar(100), Ind varchar(100), Txt varchar(max), Pid int, Pages bigint)
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
        SELECT @Tbl, name, 'ALTER INDEX '+name+' ON dbo.'+@Tbl+' REBUILD', 0, convert(bigint,9e18) FROM sys.indexes WHERE object_id = object_id(@Tbl) AND (is_disabled = 1 AND index_id > 1 AND @RebuildClustered = 0 OR index_id = 1 AND @RebuildClustered = 1)
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
            SET @Txt = 'ALTER INDEX '+@Ind+' ON dbo.'+@TblInt+' REBUILD'
            INSERT INTO @Commands SELECT @TblInt, @Ind, @Txt, @ResourceTypeId, @Pages
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
                INSERT INTO @Commands SELECT @TblInt, @Ind, @Txt, @ResourceTypeId, @Pages
                EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Add command',@Rows=@@rowcount,@Text=@Txt
              END
            END

          DELETE FROM @Indexes WHERE Ind = @Ind
        END

        IF @IndexesCnt > 1
        BEGIN
          -- add update stats so index creates are not waiting on each other
          INSERT INTO @Commands SELECT @TblInt, 'UPDATE STAT', 'UPDATE STATISTICS dbo.'+@TblInt, @ResourceTypeId, @Pages
          EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='Add command',@Rows=@@rowcount,@Text='Add stats update'
        END

        DELETE FROM @ResourceTypes WHERE ResourceTypeId = @ResourceTypeId
      END
    END

    DELETE FROM @Tables WHERE name = @Tbl
  END

  SELECT Tbl, Ind, Txt, Pid FROM @Commands ORDER BY Pages DESC, Tbl, CASE WHEN Txt LIKE 'UPDATE STAT%' THEN 0 ELSE 1 END -- update stats should be before index creates
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Commands',@Action='Select',@Rows=@@rowcount

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
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
Declare @KeyColsTable Table(KeyCol varchar(200))
Declare @IncColsTable Table(IncCol varchar(200))
Declare @Table_index Table(object_id int, index_id int)
Declare @DataComp varchar(100)
Declare @FilterDef varchar(200),@CommandForKey varchar(200),@CommandForInc varchar(200),@TblId int, @IndId int, @colname varchar(100)
Declare @PartClause varchar(100)
DECLARE @Indexes TABLE (Ind varchar(200) PRIMARY KEY, Txt varchar(max))
Declare @Temp Table(object_id int, index_id int, KeyCols varchar(200), IncCols varchar(200))

BEGIN TRY
 IF @Tbl IS NULL RAISERROR('@Tbl IS NULL',18,127)

insert into @Table_index
select I.object_id, I.index_id from sys.indexes I join sys.objects O on I.object_id=O.object_id where O.name=@Tbl and I.name=@Ind

while Exists (select * from @Table_index)
BEGIN
select top 1 @TblId = object_id, @IndId = index_id from @Table_index
set @CommandForKey = ''
set @CommandForInc = ''
Delete @KeyColsTable
insert into @KeyColsTable
select C.name
from sys.index_columns IC join sys.indexes I on IC.object_id=I.object_id and IC.index_id=I.index_id, sys.columns C
where C.column_id = IC.column_id and C.object_id=IC.object_id and IC.object_id=@TblId and IC.index_id=@IndId and IC.key_ordinal > 0 AND IC.is_included_column = 0 order by key_ordinal
while Exists (select * from @KeyColsTable)
BEGIN
select top 1 @colname=KeyCol from @KeyColsTable
set @CommandForKey = @CommandForKey+@colname+','
Delete from @KeyColsTable where KeyCol=@colname
END
Delete @IncColsTable
insert into @IncColsTable
select C.name
from sys.index_columns IC join sys.indexes I on IC.object_id=I.object_id and IC.index_id=I.index_id, sys.columns C
where C.column_id = IC.column_id and C.object_id=IC.object_id and IC.object_id=@TblId and IC.index_id=@IndId AND IC.is_included_column = 1 order by key_ordinal
while Exists(select * from @IncColsTable)
BEGIN
select top 1 @colname = IncCol from @IncColsTable
set @CommandForInc = @CommandForInc+@colname+','
Delete from @IncColsTable where IncCol=@colname
END
set @DataComp=isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = @TblId AND P.index_id = @IndId)
                                    ,(SELECT TOP 1 nullif(PropertyValue,'NONE') FROM dbo.IndexProperties, sys.objects O, sys.indexes I WHERE TableN = O.Name AND IndexName = I.Name AND O.name=@Tbl AND I.name=@Ind AND PropertyName = 'DATA_COMPRESSION')
                                    )
select @FilterDef = replace(replace(replace(replace(I.filter_definition,'[',''),']',''),'(',''),')','') from sys.indexes I where I.object_id=@TblId and I.index_id=@IndId
select @PartClause = CASE WHEN EXISTS (SELECT * FROM sys.partition_schemes S, sys.indexes I WHERE S.data_space_id = I.data_space_id AND S.name = 'PartitionScheme_ResourceTypeId'AND I.object_id=@TblId AND I.name=@Ind)  THEN ' ON PartitionScheme_ResourceTypeId (ResourceTypeId)' ELSE '' END
insert into @Indexes
select Ind=@Ind, Txt=CASE
            WHEN is_primary_key = 1
              THEN 'ALTER TABLE dbo.['+@Tbl+'] ADD PRIMARY KEY '+CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END -- Skip PK name, then this string can be applied to all component tables with no changes.
            ELSE 'CREATE'+CASE WHEN is_unique = 1 THEN ' UNIQUE' ELSE '' END+CASE WHEN I.type = 1 THEN ' CLUSTERED' ELSE '' END+' INDEX '+@Ind+' ON dbo.['+@Tbl+']'
          END
         +' ('+left(@CommandForKey, len(@CommandForKey)-1)+')'
         +CASE WHEN @CommandForInc <> '' THEN ' INCLUDE ('+left(@CommandForInc, len(@CommandForInc)-1)+')' ELSE '' END
         +CASE WHEN @FilterDef IS NOT NULL THEN ' WHERE '+@FilterDef ELSE '' END
         +CASE WHEN @DataComp IS NOT NULL THEN ' WITH (DATA_COMPRESSION = '+@DataComp+')' ELSE '' END
         +CASE WHEN @AddPartClause=1 THEN @PartClause ELSE '' END
 From sys.indexes I join sys.objects O on I.object_id=O.object_id
 where O.object_id=@TblId and I.index_id=@IndId and (@IncludeClustered=1 OR index_id>1)

 Delete from @Table_index where object_id=@TblId
END
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

    SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('''+@Tbl+''') AND name = '''+@Ind+''' AND is_disabled = 1) ALTER INDEX '+@Ind+' ON dbo.'+@Tbl+' REBUILD'
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
--IF object_id('SwitchPartitionsInAllTables') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsInAllTables
GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsInAllTables
WITH EXECUTE AS 'dbo'
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
       ,@colName varchar(200)
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
      INSERT INTO @CheckConstraints SELECT CHK.name as CheckName, CHK.definition as CheckDefinition from sys.check_constraints CHK where CHK.parent_object_id=object_id(@Tbl)
      WHILE EXISTS (SELECT * FROM @CheckConstraints)
      BEGIN
        SELECT TOP 1 @checkName=CheckName, @definition=CheckDefinition from @CheckConstraints
        SET @Txt = 'ALTER TABLE '+@TblInt+' ADD CONSTRAINT '+@checkName+'_'+convert(varchar,@ResourceTypeId)+' CHECK '+@definition
        EXECUTE(@Txt)
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt,@Action='ALTER',@Text=@Txt

        DELETE FROM @CheckConstraints WHERE CheckName=@checkName
      END
      DELETE FROM @Names
      INSERT INTO @Names SELECT name FROM sys.columns WHERE object_id = object_id(@Tbl) AND is_sparse = 1
      WHILE EXISTS (SELECT * FROM @Names) 
      BEGIN
        SET @Name = (SELECT TOP 1 name FROM @Names ORDER BY name)

        -- This is not generic but works for old anf current schema
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

--IF object_id('SwitchPartitionsOutAllTables') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsOutAllTables
GO
CREATE OR ALTER PROCEDURE dbo.SwitchPartitionsOutAllTables @RebuildClustered bit
WITH EXECUTE AS 'dbo'
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

GO
CREATE TABLE dbo.IndexProperties 
  (
     TableN     varchar(100)     NOT NULL
    ,IndexName     varchar(200)     NOT NULL
    ,PropertyName  varchar(100)     NOT NULL
    ,PropertyValue varchar(100)     NOT NULL
    ,CreateDate    datetime         NOT NULL CONSTRAINT DF_IndexProperties_CreateDate DEFAULT getUTCdate()
    
     CONSTRAINT PKC_IndexProperties_TableName_IndexName_PropertyName PRIMARY KEY CLUSTERED (TableN, IndexName, PropertyName)
  )
GO
--INSERT INTO IndexProperties (TableName,IndexName,PropertyName,PropertyValue) 
--  SELECT 'ReferenceSearchParam', 'IX_ReferenceSearchParam_SearchParamId_ReferenceResourceTypeId_ReferenceResourceId_BaseUri_ReferenceResourceVersion', 'DATA_COMPRESSION', 'PAGE'

-- (If not exist procedure InitiazlizeIndexProperties, create the procedure)
CREATE OR ALTER PROCEDURE dbo.InitializeIndexProperties
WITH EXECUTE AS 'dbo'
AS
SET NOCOUNT ON;
INSERT INTO dbo.IndexProperties 
       ( TableN, IndexName,       PropertyName,           PropertyValue ) 
  SELECT Tbl,       Ind,       'DATA_COMPRESSION', isnull(data_comp,'NONE')
    FROM (SELECT Tbl = O.Name
                ,Ind = I.Name
                ,data_comp = (SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions P WHERE P.object_id = I.object_id AND I.index_id = P.index_id)
            FROM sys.indexes I
                 JOIN sys.objects O ON O.object_id = I.object_id
            WHERE O.type = 'u'
              AND EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
         ) A
    WHERE NOT EXISTS (SELECT * FROM dbo.IndexProperties WHERE TableN = Tbl AND IndexName = Ind)
GO
