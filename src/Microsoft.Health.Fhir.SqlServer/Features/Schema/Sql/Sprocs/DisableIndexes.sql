--DROP PROCEDURE dbo.DisableIndexes
GO
CREATE PROCEDURE dbo.DisableIndexes
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = 'DisableIndexes'
       ,@Mode varchar(200) = ''
       ,@st datetime = getUTCdate()
       ,@Tbl varchar(100)
       ,@Ind varchar(200)
       ,@Txt varchar(4000)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  DECLARE @Tables TABLE (Tbl varchar(100) PRIMARY KEY, Supported bit)
  INSERT INTO @Tables EXECUTE dbo.GetPartitionedTables @IncludeNotDisabled = 1, @IncludeNotSupported = 0
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Tables',@Action='Insert',@Rows=@@rowcount

  DECLARE @Indexes TABLE (Tbl varchar(100), Ind varchar(200), TblId int, IndId int PRIMARY KEY (Tbl, Ind))
  INSERT INTO @Indexes
    SELECT Tbl
          ,I.Name
          ,TblId
          ,I.index_id
      FROM (SELECT TblId = object_id(Tbl), Tbl FROM @Tables) O
           JOIN sys.indexes I ON I.object_id = TblId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Insert',@Rows=@@rowcount

  INSERT INTO dbo.IndexProperties 
         ( TableName, IndexName,       PropertyName, PropertyValue ) 
    SELECT       Tbl,       Ind, 'DATA_COMPRESSION',     data_comp
      FROM (SELECT Tbl
                  ,Ind
                  ,data_comp = isnull((SELECT TOP 1 CASE WHEN data_compression_desc = 'PAGE' THEN 'PAGE' END FROM sys.partitions WHERE object_id = TblId AND index_id = IndId),'NONE')
              FROM @Indexes
           ) A
      WHERE NOT EXISTS (SELECT * FROM dbo.IndexProperties WHERE TableName = Tbl AND IndexName = Ind)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='IndexProperties',@Action='Insert',@Rows=@@rowcount

  DELETE FROM @Indexes WHERE Tbl IN ('Resource','ResourceCurrent','ResourceHistory') OR IndId = 1
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target='@Indexes',@Action='Delete',@Rows=@@rowcount

  WHILE EXISTS (SELECT * FROM @Indexes)
  BEGIN
    SELECT TOP 1 @Tbl = Tbl, @Ind = Ind FROM @Indexes

    SET @Txt = 'ALTER INDEX '+@Ind+' ON dbo.'+@Tbl+' DISABLE'
    EXECUTE(@Txt)
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Info',@Target=@Ind,@Action='Disable',@Text=@Txt

    DELETE FROM @Indexes WHERE Tbl = @Tbl AND Ind = @Ind
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
--INSERT INTO Parameters (Id,Char) SELECT name,'LogEvent' FROM sys.objects WHERE type = 'p'
--SELECT TOP 100 * FROM EventLog ORDER BY EventDate DESC
