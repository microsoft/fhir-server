--IF object_id('SwitchPartitionsIn') IS NOT NULL DROP PROCEDURE dbo.SwitchPartitionsIn
GO
CREATE PROCEDURE dbo.SwitchPartitionsIn @Tbl varchar(100), @OneResourceTypeId smallint = NULL, @InTbl varchar(100) = NULL
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'SwitchPartitionsIn'
       ,@Mode varchar(200) = 'Tbl='+isnull(@Tbl,'NULL')+' ORT='+isnull(convert(varchar,@OneResourceTypeId),'NULL')+' InTbl='+isnull(@InTbl,'NULL')
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

  SET @InTbl = isnull(@InTbl,@Tbl)

  -- Partitioned table should be either empty, then rebuilt indexes, or all indexes are already built
  INSERT INTO @Indexes SELECT index_id, name FROM sys.indexes WHERE object_id = object_id(@InTbl) AND is_disabled = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Indexes) -- This is irrelevant if @InTbl is specified
  BEGIN
    SELECT TOP 1 @IndId = IndId, @Ind = name FROM @Indexes ORDER BY IndId

    SET @DataComp = CASE WHEN (SELECT PropertyValue FROM dbo.IndexProperties WHERE TableName=@Tbl AND IndexName=@Ind)='PAGE' THEN ' PARTITION = ALL WITH (DATA_COMPRESSION = PAGE)' ELSE '' END
    SET @Txt = 'IF EXISTS (SELECT * FROM sys.indexes WHERE object_id = object_id('''+@Tbl+''') AND name = '''+@Ind+''' AND is_disabled = 1) ALTER INDEX '+@Ind+' ON dbo.'+@Tbl+' REBUILD'+@DataComp
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Ind,@Action='Rebuild',@Text=@Txt

    DELETE FROM @Indexes WHERE IndId = @IndId
  END

  INSERT INTO @ResourceTypes
    SELECT ResourceTypeId
      FROM (SELECT ResourceTypeId = convert(smallint,reverse(substring(revName,1,charindex('_',revName)-1))) -- break by last _
              FROM (SELECT *, revName = reverse(name) FROM sys.objects) O
              WHERE name LIKE @Tbl+'[_]%[0-9]'
                AND (EXISTS (SELECT * FROM sysindexes WHERE id = O.object_id AND indid IN (0,1) AND rows > 0) OR @OneResourceTypeId IS NOT NULL)
           ) A
      WHERE @OneResourceTypeId IS NULL OR @OneResourceTypeId = ResourceTypeId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='#ResourceTypes',@Action='Select Into',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @ResourceTypes)
  BEGIN
    SET @ResourceTypeId = (SELECT TOP 1 ResourceTypeId FROM @ResourceTypes)
    SET @TblInt = @Tbl+'_'+convert(varchar,@ResourceTypeId)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@TblInt

    SET @Txt = 'ALTER TABLE dbo.'+@TblInt+' SWITCH TO dbo.'+@InTbl+' PARTITION $partition.PartitionFunction_ResourceTypeId('+convert(varchar,@ResourceTypeId)+')'
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@InTbl,@Action='Switch in start',@Text=@Txt
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@InTbl,@Action='Switch in',@Text=@Txt

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
