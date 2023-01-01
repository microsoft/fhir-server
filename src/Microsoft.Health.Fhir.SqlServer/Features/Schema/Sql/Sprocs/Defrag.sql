--DROP PROCEDURE dbo.Defrag
GO
CREATE PROCEDURE dbo.Defrag @TableName varchar(100), @IndexName varchar(200), @PartitionNumber int, @IsPartitioned bit
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
