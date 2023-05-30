--DROP PROCEDURE dbo.DisableIndexes
GO
CREATE OR ALTER PROCEDURE dbo.DisableIndexes
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

  DELETE FROM @Indexes WHERE Tbl = 'Resource' OR IndId = 1
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
--DROP PROCEDURE dbo.DequeueJob
GO
CREATE OR ALTER PROCEDURE dbo.DequeueJob @QueueType tinyint, @Worker varchar(100), @HeartbeatTimeoutSec int, @InputJobId bigint = NULL, @CheckTimeoutJobs bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'DequeueJob'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' H='+isnull(convert(varchar,@HeartbeatTimeoutSec),'NULL')
                           +' W='+isnull(@Worker,'NULL')
                           +' IJ='+isnull(convert(varchar,@InputJobId),'NULL')
                           +' T='+isnull(convert(varchar,@CheckTimeoutJobs),'NULL')
       ,@Rows int = 0
       ,@st datetime = getUTCdate()
       ,@JobId bigint
       ,@msg varchar(100)
       ,@Lock varchar(100)
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0

BEGIN TRY
  IF EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'DequeueJobStop' AND Number = 1)
  BEGIN
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=0,@Text='Skipped'
    RETURN
  END

  IF @InputJobId IS NULL
    SET @PartitionId = @MaxPartitions * rand()
  ELSE 
    SET @PartitionId = @InputJobId % 16

  SET TRANSACTION ISOLATION LEVEL READ COMMITTED 

  WHILE @InputJobId IS NULL AND @JobId IS NULL AND @LookedAtPartitions <= @MaxPartitions AND @CheckTimeoutJobs = 0
  BEGIN
    SET @Lock = 'DequeueJob_'+convert(varchar,@QueueType)+'_'+convert(varchar,@PartitionId)

    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    UPDATE T
      SET StartDate = getUTCdate()
         ,HeartbeatDate = getUTCdate()
         ,Worker = @Worker 
         ,Status = 1 -- running
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
         ,@JobId = T.JobId
      FROM dbo.JobQueue T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        JobId
                   FROM dbo.JobQueue WITH (INDEX = IX_QueueType_PartitionId_Status_Priority)
                   WHERE QueueType = @QueueType
                     AND PartitionId = @PartitionId
                     AND Status = 0
                   ORDER BY 
                        Priority
                       ,JobId
                ) S
             ON QueueType = @QueueType AND PartitionId = @PartitionId AND T.JobId = S.JobId
    SET @Rows += @@rowcount

    COMMIT TRANSACTION

    IF @JobId IS NULL
    BEGIN
      SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
      SET @LookedAtPartitions = @LookedAtPartitions + 1 
    END
  END

  -- Do timed out items. 
  SET @LookedAtPartitions = 0
  WHILE @InputJobId IS NULL AND @JobId IS NULL AND @LookedAtPartitions <= @MaxPartitions
  BEGIN
    SET @Lock = 'DequeueStoreCopyWorkUnit_'+convert(varchar, @PartitionId)

    BEGIN TRANSACTION  

    EXECUTE sp_getapplock @Lock, 'Exclusive'

    UPDATE T
      SET StartDate = getUTCdate()
         ,HeartbeatDate = getUTCdate()
         ,Worker = @Worker 
         ,Status = CASE WHEN CancelRequested = 0 THEN 1 ELSE 4 END 
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
         ,@JobId = CASE WHEN CancelRequested = 0 THEN T.JobId END
         ,Info = convert(varchar(1000),isnull(Info,'')+' Prev: Worker='+Worker+' Start='+convert(varchar,StartDate,121))
      FROM dbo.JobQueue T WITH (PAGLOCK)
           JOIN (SELECT TOP 1 
                        JobId
                   FROM dbo.JobQueue WITH (INDEX = IX_QueueType_PartitionId_Status_Priority)
                   WHERE QueueType = @QueueType
                     AND PartitionId = @PartitionId
                     AND Status = 1
                     AND datediff(second,HeartbeatDate,getUTCdate()) > @HeartbeatTimeoutSec
                   ORDER BY 
                        Priority
                       ,JobId
                ) S
             ON QueueType = @QueueType AND PartitionId = @PartitionId AND T.JobId = S.JobId
    SET @Rows += @@rowcount

    COMMIT TRANSACTION

    IF @JobId IS NULL
    BEGIN
      SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
      SET @LookedAtPartitions = @LookedAtPartitions + 1 
    END
  END

  IF @InputJobId IS NOT NULL
  BEGIN
    UPDATE dbo.JobQueue WITH (PAGLOCK)
      SET StartDate = getUTCdate()
         ,HeartbeatDate = getUTCdate()
         ,Worker = @Worker 
         ,Status = 1 -- running
         ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
         ,@JobId = JobId
      WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND Status = 0 AND JobId = @InputJobId 
    SET @Rows += @@rowcount

    IF @JobId IS NULL
    BEGIN
      UPDATE dbo.JobQueue WITH (PAGLOCK)
        SET StartDate = getUTCdate()
           ,HeartbeatDate = getUTCdate()
           ,Worker = @Worker 
           ,Status = 1 -- running
           ,Version = datediff_big(millisecond,'0001-01-01',getUTCdate())
           ,@JobId = JobId
        WHERE QueueType = @QueueType AND PartitionId = @PartitionId AND Status = 1 AND JobId = @InputJobId
          AND datediff(second,HeartbeatDate,getUTCdate()) > @HeartbeatTimeoutSec
      SET @Rows += @@rowcount
    END
  END

  IF @JobId IS NOT NULL
    EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobId = @JobId
  
  SET @msg = 'J='+isnull(convert(varchar,@JobId),'NULL')+' P='+convert(varchar,@PartitionId)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO

