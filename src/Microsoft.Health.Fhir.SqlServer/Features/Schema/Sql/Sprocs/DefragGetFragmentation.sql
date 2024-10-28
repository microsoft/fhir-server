--DROP PROCEDURE dbo.DefragGetFragmentation
GO
CREATE PROCEDURE dbo.DefragGetFragmentation @TableName varchar(200), @IndexName varchar(200) = NULL, @PartitionNumber int = NULL
WITH EXECUTE AS 'dbo'
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)
       ,@Rows int
       ,@MinFragPct int = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.MinFragPct'),10)
       ,@MinSizeGB float = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'Defrag.MinSizeGB'),0.1)
       ,@PreviousGroupId bigint
       ,@IndexId int

DECLARE @Mode varchar(200) = 'T='+@TableName+' I='+isnull(@IndexName,'NULL')+' P='+isnull(convert(varchar,@PartitionNumber),'NULL')+' MF='+convert(varchar,@MinFragPct)+' MS='+convert(varchar,@MinSizeGB)

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start'

  IF object_id(@TableName) IS NULL
    RAISERROR('Table does not exist',18,127)
  
  SET @IndexId = (SELECT index_id FROM sys.indexes WHERE object_id = object_id(@TableName) AND name = @IndexName)
  IF @IndexName IS NOT NULL AND @IndexId IS NULL
    RAISERROR('Index does not exist',18,127)

  -- find closest archived group id for this table
  SET @PreviousGroupId = (SELECT TOP 1 GroupId FROM dbo.JobQueue WHERE QueueType = 3 AND Status = 5 AND Definition = @TableName ORDER BY GroupId DESC)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='@PreviousGroupId',@Text=@PreviousGroupId

  SELECT TableName
        ,IndexName
        ,partition_number
        ,frag_in_percent
    FROM (SELECT TableName = @TableName
                ,IndexName = I.name
                ,partition_number
                ,frag_in_percent = avg_fragmentation_in_percent
                ,prev_frag_in_percent = isnull(convert(float, Result),0)
            FROM (SELECT object_id, index_id, partition_number, avg_fragmentation_in_percent
                    FROM sys.dm_db_index_physical_stats(db_id(), object_id(@TableName), @IndexId, @PartitionNumber, 'LIMITED') A
                    WHERE index_id > 0
                      AND (@PartitionNumber IS NOT NULL OR avg_fragmentation_in_percent >= @MinFragPct AND A.page_count > @MinSizeGB*1024*1024/8)
                 ) A
                 JOIN sys.indexes I ON I.object_id = A.object_id AND I.index_id = A.index_id
                 LEFT OUTER JOIN dbo.JobQueue ON QueueType = 3 AND Status = 5 AND GroupId = @PreviousGroupId AND Definition = I.name+';'+convert(varchar,partition_number)
         ) A
    WHERE @PartitionNumber IS NOT NULL OR frag_in_percent >= prev_frag_in_percent + @MinFragPct

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
-- INSERT INTO Parameters (Id,Char) SELECT 'DefragGetFragmentation', 'LogEvent'
-- EXECUTE dbo.DefragGetFragmentation @TableName = 'ResourceTbl'
-- SELECT * FROM EventLog WHERE EventDate > dateadd(hour,-1,getUTCdate()) AND Process = 'DefragGetFragmentation' ORDER BY EventDate DESC
