--IF object_id('GetCommandsForRebuildIndexes') IS NOT NULL DROP PROCEDURE dbo.GetCommandsForRebuildIndexes
GO
CREATE PROCEDURE dbo.GetCommandsForRebuildIndexes @RebuildClustered bit
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
