--IF object_id('SwitchPartitionsOut') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsOut
GO
CREATE PROCEDURE dbo.SwitchPartitionsOut @Tbl varchar(100), @RebuildClustered bit
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

DECLARE @Indexes TABLE (IndId int PRIMARY KEY, name varchar(200), IsDisabled bit)
DECLARE @IndexesRT TABLE (IndId int PRIMARY KEY, name varchar(200), IsDisabled bit)
DECLARE @ResourceTypes TABLE (ResourceTypeId smallint PRIMARY KEY, partition_number_roundtrip int, partition_number int, row_count bigint)
DECLARE @Names TABLE (name varchar(100) PRIMARY KEY)

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
