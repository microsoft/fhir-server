--DROP PROCEDURE dbo.InitDefrag
GO
CREATE PROCEDURE dbo.InitDefrag @GroupId bigint, @MinFragPct int = 10, @MinSizeGB float = 1
WITH EXECUTE AS SELF
AS
set nocount on
DECLARE @SP varchar(100) = 'InitDefrag'
       ,@Mode varchar(200) = 'G='+convert(varchar,@GroupId)+' MF='+convert(varchar,@MinFragPct)+' MS='+convert(varchar,@MinSizeGB)
       ,@st datetime = getUTCdate()
       ,@ObjectId int
       ,@msg varchar(1000)
       ,@Rows int
       ,@QueueType tinyint = 200 -- TODO: Replace with real
       ,@DefinitionsSorted StringList

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
  SET @Rows = @@rowcount

  IF @Rows > 0
    EXECUTE dbo.EnqueueJobs @QueueType = @QueueType, @Definitions = @DefinitionsSorted, @GroupId = @GroupId, @ForceOneActiveJobGroup = 1

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--INSERT INTO Parameters (Id,Char) SELECT 'Defrag','LogEvent'
--SELECT TOP 200 * FROM EventLog ORDER BY EventDate DESC
