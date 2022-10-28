--DROP TABLE dbo.Watchdogs
GO
IF object_id('dbo.Watchdogs') IS NULL 
CREATE TABLE dbo.Watchdogs
  (
       Watchdog            varchar(100)  NOT NULL
      ,LeaseHolder         varchar(100)  NOT NULL CONSTRAINT DF_Watchdogs_LeaseHolder DEFAULT ''
      ,LeaseEndDate        datetime      NOT NULL CONSTRAINT DF_Watchdogs_LeaseEndDate DEFAULT 0
      ,RemainingLeaseSec   AS datediff(second,getUTCdate(),LeaseEndDate)
      ,LeaseRequestor      varchar(100)  NOT NULL CONSTRAINT DF_Watchdogs_LeaseRequestor DEFAULT ''
      ,LeaseRequestDate    datetime      NOT NULL CONSTRAINT DF_Watchdogs_LeaseRequestDate DEFAULT 0
      ,LastExecutionDate   datetime      NULL

	      CONSTRAINT PKC_Watchdogs_Watchdog PRIMARY KEY CLUSTERED (Watchdog)
  )
GO
--DROP PROCEDURE AcquireWatchdogLease
GO
CREATE OR ALTER PROCEDURE dbo.AcquireWatchdogLease
   @Watchdog           varchar(100)
  ,@Worker             varchar(100)
  ,@LeasePeriodSec     int 
  ,@AllowRebalancing   bit
  ,@ForceAcquire       bit          = 0 
  ,@WorkerIsRunning    bit          = 0 
  ,@LeaseEndDate       datetime            OUT
  ,@IsAcquired         bit                 OUT
  ,@CurrentLeaseHolder varchar(100) = NULL OUT
  ,@LastExecutionDate  datetime     = NULL OUT
AS
set nocount on
set xact_abort on
DECLARE @SP varchar(100) = 'AcquireWatchdogLease'
       ,@Mode varchar(100)
       ,@msg varchar(1000)
       ,@MyLeasesNumber int
       ,@OtherValidRequestsOrLeasesNumber int
       ,@MyValidRequestsOrLeasesNumber int
       ,@DesiredLeasesNumber int
       ,@NotLeasedWatchdogNumber int
       ,@WatchdogNumber int
       ,@Now datetime
       ,@MyLastChangeTime datetime
       ,@PreviousLeaseHolder varchar(100)
       ,@Rows int = 0
       ,@NumberOfWorkers int
       ,@st datetime = getUTCdate()
       ,@RowsInt int
       ,@Pattern varchar(100)

BEGIN TRY
  SET @Mode = 'R='+isnull(@Watchdog,'NULL')+' W='+isnull(@Worker,'NULL')+' F='+isnull(convert(varchar,@ForceAcquire),'NULL')+' LP='+isnull(convert(varchar,@LeasePeriodSec),'NULL')
  
  SET @CurrentLeaseHolder = ''
  SET @IsAcquired = 0
  SET @Now = getUTCdate()
  SET @LeaseEndDate = @Now

  -- Look for Watchdog specific pattern first
  SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderIncludePatternFor'+@Watchdog),'')
  IF @Pattern IS NULL
    SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderIncludePattern'),'')
  IF @Pattern IS NOT NULL AND @Worker NOT LIKE @Pattern
  BEGIN
    SET @msg = 'Worker does not match include pattern='+@Pattern
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
    SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder FROM dbo.Watchdogs WHERE Watchdog = @Watchdog),'')
    RETURN
  END

  SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderExcludePatternFor'+@Watchdog),'')
  IF @Pattern IS NULL
    SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderExcludePattern'),'')
  IF @Pattern IS NOT NULL AND @Worker LIKE @Pattern
  BEGIN
    SET @msg = 'Worker matches exclude pattern='+@Pattern
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
    SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder FROM dbo.Watchdogs WHERE Watchdog = @Watchdog),'')
    RETURN
  END

  -- Remove leases
  DECLARE @Watchdogs TABLE (Watchdog varchar(100) PRIMARY KEY)
  INSERT INTO @Watchdogs
    SELECT Watchdog 
      FROM dbo.Watchdogs WITH (NOLOCK) 
      WHERE RemainingLeaseSec*(-1) > 10*@LeasePeriodSec
         OR @ForceAcquire = 1 AND Watchdog = @Watchdog AND LeaseHolder <> @Worker
  IF @@rowcount > 0
  BEGIN 
    DELETE FROM dbo.Watchdogs WHERE Watchdog IN (SELECT Watchdog FROM @Watchdogs)
    SET @Rows += @@rowcount

    IF @Rows > 0
    BEGIN
      SET @msg = ''
      SELECT @msg = convert(varchar(1000),@msg + CASE WHEN @msg = '' THEN '' ELSE ',' END + Watchdog) FROM @Watchdogs
      SET @msg = convert(varchar(1000),'Remove old/forced leases:'+@msg)
      EXECUTE dbo.LogEvent @Process='AcquireWatchdogLease',@Status='Info',@Mode=@Mode,@Target='WatchdogLeases',@Action='Delete',@Rows=@Rows,@Text=@msg
    END
  END

  SET @NumberOfWorkers = 1 + (SELECT count(*) 
                                FROM (SELECT LeaseHolder FROM dbo.Watchdogs WITH (NOLOCK) WHERE LeaseHolder <> @Worker
                                      UNION 
                                      SELECT LeaseRequestor FROM dbo.Watchdogs WITH (NOLOCK) WHERE LeaseRequestor <> @Worker AND LeaseRequestor <> ''
                                     ) A
                             )

  SET @Mode = convert(varchar(100),@Mode+' N='+convert(varchar(10),@NumberOfWorkers))

  -- Prepare the row in table - TABLOCKX
  IF NOT EXISTS (SELECT * FROM dbo.Watchdogs WITH (NOLOCK) WHERE Watchdog = @Watchdog) -- Minimize time when TABLOCKX is needed
    INSERT INTO dbo.Watchdogs (Watchdog, LeaseEndDate, LeaseRequestTime) 
      SELECT @Watchdog, dateadd(day,-10,@Now), dateadd(day,-10,@Now) 
        WHERE NOT EXISTS (SELECT * FROM dbo.Watchdogs WITH (TABLOCKX) WHERE Watchdog = @Watchdog)
  
  SET @LeaseEndDate = dateadd(second,@LeasePeriodSec,@Now)
  
  SET @WatchdogNumber = (SELECT count(*) FROM dbo.Watchdogs WITH (NOLOCK))
  --PRINT '@WatchdogNumber = '+convert(varchar,@WatchdogNumber)

  SET @NotLeasedWatchdogNumber = (SELECT count(*) FROM dbo.Watchdogs WITH (NOLOCK) WHERE LeaseHolder = '' OR LeaseEndDate < @Now)
  --PRINT '@NotLeasedWatchdogNumber = '+convert(varchar,@NotLeasedWatchdogNumber)
  
  SET @MyLeasesNumber = (SELECT count(*) FROM dbo.Watchdogs WITH (NOLOCK) WHERE LeaseHolder = @Worker AND LeaseEndDate > @Now)
  --PRINT '@MyLeasesNumber = '+convert(varchar,@MyLeasesNumber)
  
  SET @OtherValidRequestsOrLeasesNumber = 
        (SELECT count(*) 
           FROM dbo.Watchdogs WITH (NOLOCK)
           WHERE LeaseHolder <> @Worker AND LeaseEndDate > @Now 
              OR LeaseRequestor <> @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec
        )
  --PRINT '@OtherValidRequestsOrLeasesNumber = '+convert(varchar,@OtherValidRequestsOrLeasesNumber)
  
  SET @MyValidRequestsOrLeasesNumber = 
        (SELECT count(*) 
           FROM dbo.Watchdogs WITH (NOLOCK)
           WHERE LeaseHolder = @Worker AND LeaseEndDate > @Now 
              OR LeaseRequestor = @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec
        )
  --PRINT '@MyValidRequestsOrLeasesNumber = '+convert(varchar,@MyValidRequestsOrLeasesNumber)
  
  SET @DesiredLeasesNumber = ceiling(1.0 * @WatchdogNumber / @NumberOfWorkers)
  IF @DesiredLeasesNumber = 0 SET @DesiredLeasesNumber = 1
  IF @DesiredLeasesNumber = 1 AND @OtherValidRequestsOrLeasesNumber = 1 AND @WatchdogNumber = 1 SET @DesiredLeasesNumber = 0
  IF @MyValidRequestsOrLeasesNumber = floor(1.0 * @WatchdogNumber / @NumberOfWorkers)
     AND @OtherValidRequestsOrLeasesNumber + @MyValidRequestsOrLeasesNumber = @WatchdogNumber
    SET @DesiredLeasesNumber = @DesiredLeasesNumber - 1
  --PRINT '@DesiredLeasesNumber = '+convert(varchar,@DesiredLeasesNumber)
  
  UPDATE dbo.Watchdogs
    SET LeaseHolder = @Worker
       ,LeaseEndDate = @LeaseEndDate
       ,LeaseRequestor = ''
       ,@PreviousLeaseHolder = LeaseHolder
       ,LastExecutionDate = CASE WHEN @LastExecutionDate > LastExecutionDate THEN @LastExecutionDate ELSE LastExecutionDate END
       ,@LastExecutionDate = CASE WHEN @LastExecutionDate > LastExecutionDate THEN @LastExecutionDate ELSE LastExecutionDate END
    WHERE Watchdog = @Watchdog
      AND (@WorkerIsRunning = 1 OR NOT (LeaseRequestor <> @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec)) -- no valid request to release a lease from others
      AND ( -- lease renew logic
            LeaseHolder = @Worker -- me
              AND LeaseEndDate > @Now -- still valid
            OR 
            -- acquire new lease logic
            LeaseEndDate < @Now -- expired
              AND ( @DesiredLeasesNumber > @MyLeasesNumber -- not enough Watchdogs
                    OR 
                    @OtherValidRequestsOrLeasesNumber < @WatchdogNumber -- there is a room for leases
                  )
          )
  IF @@rowcount > 0 
  BEGIN 
    SET @IsAcquired = 1
    SET @msg = 'Lease holder changed from ['+isnull(@PreviousLeaseHolder,'')+'] to ['+@Worker+']' 
    IF @PreviousLeaseHolder <> @Worker
      EXECUTE dbo.LogEvent @Process='AcquireWatchdogLease',@Status='Info',@Mode=@Mode,@Text=@msg
  END
  ELSE
    IF @AllowRebalancing = 1 
    BEGIN
      SET @CurrentLeaseHolder = (SELECT LeaseHolder FROM dbo.Watchdogs WHERE Watchdog = @Watchdog)

      -- refresh request
      UPDATE dbo.Watchdogs
        SET LeaseRequestTime = @Now
        WHERE Watchdog = @Watchdog
          AND LeaseRequestor = @Worker
          AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec
    
      IF @DesiredLeasesNumber > @MyValidRequestsOrLeasesNumber
      BEGIN
        -- request
        UPDATE A
          SET LeaseRequestor = @Worker
             ,LeaseRequestTime = @Now
          FROM dbo.Watchdogs A
          WHERE Watchdog = @Watchdog
            AND NOT (LeaseRequestor <> @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec) -- no valid request to release a lease from others
            AND @NotLeasedWatchdogNumber = 0
            AND (SELECT count(*) FROM dbo.Watchdogs B WHERE B.LeaseHolder = A.LeaseHolder AND datediff(second,B.LeaseEndDate,@Now) < @LeasePeriodSec) > @DesiredLeasesNumber -- current guy holds more than required
        SET @RowsInt = @@rowcount
        SET @msg = '@DesiredLeasesNumber=['+convert(varchar(10),@DesiredLeasesNumber)+'] > @MyValidRequestsOrLeasesNumber=['+convert(varchar(10),@MyValidRequestsOrLeasesNumber)+']' 
        EXECUTE dbo.LogEvent @Process='AcquireWatchdogLease',@Status='Info',@Mode=@Mode,@Rows=@RowsInt,@Text=@msg
      END
    END
  
  SET @Mode = convert(varchar(100),@Mode+' A='+convert(varchar(1),@IsAcquired))
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process='AcquireWatchdogLease',@Status='Error',@Mode=@Mode;
  THROW
END CATCH
GO
--DECLARE @LeaseEndDate  datetime
--       ,@IsAcquired     bit
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'whatever'
--       ,@Worker = 'box.1234'
--       ,@NumberOfWorkers = 4
--       ,@LeasePeriodSec = 120
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT

--DELETE FROM WatchdogLeases WHERE Id IN ('R1','R2')
--DECLARE @LeaseEndDate  datetime
--       ,@IsAcquired     bit
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R1 by P1', @LeaseEndDate, @IsAcquired
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R2 by P1', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire R1 by P2 while P1 lease is valid', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire R2 by P2', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--WAITFOR DELAY '00:00:02'
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to renew lease on R1 by P1 while P1 lease is valid', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--WAITFOR DELAY '00:00:04'
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire R1 by P2 when P1 lease expired', @LeaseEndDate, @IsAcquired
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to renew R2 by P2', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases
--WAITFOR DELAY '00:00:02'
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to renew R2 by P2', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R1 by P1', @LeaseEndDate, @IsAcquired
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndDate = @LeaseEndDate OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R2 by P1', @LeaseEndDate, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--SELECT TOP 10 * FROM EventLog WHERE Process = 'AcquireWatchdogLease' ORDER BY EventDate DESC
----SELECT TOP 10 * FROM EventLog ORDER BY EventId DESC
--SELECT count(*) FROM EventLog WHERE Process = 'AcquireWatchdogLease'
--DELETE TOP (100000) FROM EventLog WHERE Process = 'AcquireWatchdogLease'
GO
--DROP PROCEDURE dbo.ArchiveJobs
GO
CREATE OR ALTER PROCEDURE dbo.ArchiveJobs @QueueType tinyint
AS
set nocount on
DECLARE @SP varchar(100) = 'ArchiveJobs'
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@InflightRows int = 0
       ,@Lock varchar(100) = 'DequeueJob_'+convert(varchar,@QueueType)

BEGIN TRY
  SET @PartitionId = @MaxPartitions * rand()

  BEGIN TRANSACTION
  
  EXECUTE sp_getapplock @Lock, 'Exclusive'

  WHILE @LookedAtPartitions <= @MaxPartitions
  BEGIN
    SET @InflightRows += (SELECT count(*) FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND Status IN (0,1))

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions = @LookedAtPartitions + 1 
  END

  IF @InflightRows = 0
  BEGIN
    SET @LookedAtPartitions = 0
    WHILE @LookedAtPartitions <= @MaxPartitions
    BEGIN
      UPDATE dbo.JobQueue
        SET Status = 5
        WHERE PartitionId = @PartitionId
          AND QueueType = @QueueType
          AND Status IN (2,3,4)
      SET @Rows += @@rowcount

      SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
      SET @LookedAtPartitions = @LookedAtPartitions + 1 
    END
  END

  COMMIT TRANSACTION

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DROP PROCEDURE dbo.InitDefrag
GO
CREATE OR ALTER PROCEDURE dbo.InitDefrag @QueueType tinyint, @GroupId bigint
WITH EXECUTE AS SELF
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
--DROP PROCEDURE dbo.GetActiveJobs
GO
CREATE OR ALTER PROCEDURE dbo.GetActiveJobs @QueueType tinyint, @GroupId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'GetActiveJobs'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' G='+isnull(convert(varchar,@GroupId),'NULL')
       ,@st datetime = getUTCdate()
       ,@JobIds BigintList
       ,@PartitionId tinyint
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@Rows int = 0

BEGIN TRY
  SET @PartitionId = @MaxPartitions * rand()

  WHILE @LookedAtPartitions <= @MaxPartitions
  BEGIN
    IF @GroupId IS NULL
      INSERT INTO @JobIds SELECT JobId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND Status IN (0,1)
    ELSE
      INSERT INTO @JobIds SELECT JobId FROM dbo.JobQueue WHERE PartitionId = @PartitionId AND QueueType = @QueueType AND GroupId = @GroupId AND Status IN (0,1)

    SET @Rows += @@rowcount

    SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
    SET @LookedAtPartitions += 1 
  END

  IF @Rows > 0
    EXECUTE dbo.GetJobs @QueueType = @QueueType, @JobIds = @JobIds

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DROP PROCEDURE dbo.DefragChangeDatabaseSettings
GO
CREATE OR ALTER PROCEDURE dbo.DefragChangeDatabaseSettings @IsOn bit
WITH EXECUTE AS SELF
AS
set nocount on
DECLARE @SP varchar(100) = 'DefragChangeDatabaseSettings'
       ,@Mode varchar(200) = 'On='+convert(varchar,@IsOn)
       ,@st datetime = getUTCdate()
       ,@db varchar(100) = quotename(db_name())
       ,@SQL varchar(3500) 

BEGIN TRY
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Start',@Mode=@Mode

  SET @SQL = 'ALTER DATABASE '+@db+' SET AUTO_UPDATE_STATISTICS '+CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END
  EXECUTE(@SQL)
  EXECUTE dbo.LogEvent @Process=@SP,@Status='Run',@Mode=@Mode,@Text=@SQL

  SET @SQL = 'ALTER DATABASE '+@db+' SET AUTO_CREATE_STATISTICS '+CASE WHEN @IsOn = 1 THEN 'ON' ELSE 'OFF' END
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
WITH EXECUTE AS SELF
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
--DROP PROCEDURE dbo.DequeueJob
GO
CREATE OR ALTER PROCEDURE dbo.DequeueJob @QueueType tinyint, @Worker varchar(100), @HeartbeatTimeoutSec int, @InputJobId bigint = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'DequeueJob'
       ,@Mode varchar(100) = 'Q='+isnull(convert(varchar,@QueueType),'NULL')
                           +' H='+isnull(convert(varchar,@HeartbeatTimeoutSec),'NULL')
                           +' W='+isnull(@Worker,'NULL')
                           +' IJ='+isnull(convert(varchar,@InputJobId),'NULL')
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
    RETURN

  IF @InputJobId IS NULL
    SET @PartitionId = @MaxPartitions * rand()
  ELSE 
    SET @PartitionId = @InputJobId % 16

  SET TRANSACTION ISOLATION LEVEL READ COMMITTED 

  WHILE @InputJobId IS NULL AND @JobId IS NULL AND @LookedAtPartitions <= @MaxPartitions
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
