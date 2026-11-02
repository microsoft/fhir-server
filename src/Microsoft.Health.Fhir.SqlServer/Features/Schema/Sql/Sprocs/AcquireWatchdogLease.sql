--DROP PROCEDURE AcquireWatchdogLease
GO
CREATE PROCEDURE dbo.AcquireWatchdogLease
   @Watchdog           varchar(100)
  ,@Worker             varchar(100)
  ,@AllowRebalance     bit          = 1 
  ,@ForceAcquire       bit          = 0 
  ,@LeasePeriodSec     float 
  ,@WorkerIsRunning    bit          = 0 
  ,@LeaseEndTime       datetime            OUT
  ,@IsAcquired         bit                 OUT
  ,@CurrentLeaseHolder varchar(100) = NULL OUT
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
  SET @LeaseEndTime = @Now

  -- Look for Watchdog specific pattern first
  SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderIncludePatternFor'+@Watchdog),'')
  IF @Pattern IS NULL
    SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderIncludePattern'),'')
  IF @Pattern IS NOT NULL AND @Worker NOT LIKE @Pattern
  BEGIN
    SET @msg = 'Worker does not match include pattern='+@Pattern
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
    SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder FROM dbo.WatchdogLeases WHERE Watchdog = @Watchdog),'')
    RETURN
  END

  SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderExcludePatternFor'+@Watchdog),'')
  IF @Pattern IS NULL
    SET @Pattern = nullif((SELECT Char FROM dbo.Parameters WHERE Id = 'WatchdogLeaseHolderExcludePattern'),'')
  IF @Pattern IS NOT NULL AND @Worker LIKE @Pattern
  BEGIN
    SET @msg = 'Worker matches exclude pattern='+@Pattern
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
    SET @CurrentLeaseHolder = isnull((SELECT LeaseHolder FROM dbo.WatchdogLeases WHERE Watchdog = @Watchdog),'')
    RETURN
  END

  -- Remove leases
  DECLARE @Watchdogs TABLE (Watchdog varchar(100) PRIMARY KEY)
  INSERT INTO @Watchdogs
    SELECT Watchdog 
      FROM dbo.WatchdogLeases WITH (NOLOCK) 
      WHERE RemainingLeaseTimeSec*(-1) > 10*@LeasePeriodSec
         OR @ForceAcquire = 1 AND Watchdog = @Watchdog AND LeaseHolder <> @Worker
  IF @@rowcount > 0
  BEGIN 
    DELETE FROM dbo.WatchdogLeases WHERE Watchdog IN (SELECT Watchdog FROM @Watchdogs)
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
                                FROM (SELECT LeaseHolder FROM dbo.WatchdogLeases WITH (NOLOCK) WHERE LeaseHolder <> @Worker
                                      UNION 
                                      SELECT LeaseRequestor FROM dbo.WatchdogLeases WITH (NOLOCK) WHERE LeaseRequestor <> @Worker AND LeaseRequestor <> ''
                                     ) A
                             )

  SET @Mode = convert(varchar(100),@Mode+' N='+convert(varchar(10),@NumberOfWorkers))

  -- Prepare the row in table - TABLOCKX
  IF NOT EXISTS (SELECT * FROM dbo.WatchdogLeases WITH (NOLOCK) WHERE Watchdog = @Watchdog) -- Minimize time when TABLOCKX is needed
    INSERT INTO dbo.WatchdogLeases (Watchdog, LeaseEndTime, LeaseRequestTime) 
      SELECT @Watchdog, dateadd(day,-10,@Now), dateadd(day,-10,@Now) 
        WHERE NOT EXISTS (SELECT * FROM dbo.WatchdogLeases WITH (TABLOCKX) WHERE Watchdog = @Watchdog)
  
  SET @LeaseEndTime = dateadd(second,@LeasePeriodSec,@Now)
  
  SET @WatchdogNumber = (SELECT count(*) FROM dbo.WatchdogLeases WITH (NOLOCK))
  --PRINT '@WatchdogNumber = '+convert(varchar,@WatchdogNumber)

  SET @NotLeasedWatchdogNumber = (SELECT count(*) FROM dbo.WatchdogLeases WITH (NOLOCK) WHERE LeaseHolder = '' OR LeaseEndTime < @Now)
  --PRINT '@NotLeasedWatchdogNumber = '+convert(varchar,@NotLeasedWatchdogNumber)
  
  SET @MyLeasesNumber = (SELECT count(*) FROM dbo.WatchdogLeases WITH (NOLOCK) WHERE LeaseHolder = @Worker AND LeaseEndTime > @Now)
  --PRINT '@MyLeasesNumber = '+convert(varchar,@MyLeasesNumber)
  
  SET @OtherValidRequestsOrLeasesNumber = 
        (SELECT count(*) 
           FROM dbo.WatchdogLeases WITH (NOLOCK)
           WHERE LeaseHolder <> @Worker AND LeaseEndTime > @Now 
              OR LeaseRequestor <> @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec
        )
  --PRINT '@OtherValidRequestsOrLeasesNumber = '+convert(varchar,@OtherValidRequestsOrLeasesNumber)
  
  SET @MyValidRequestsOrLeasesNumber = 
        (SELECT count(*) 
           FROM dbo.WatchdogLeases WITH (NOLOCK)
           WHERE LeaseHolder = @Worker AND LeaseEndTime > @Now 
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
  
  UPDATE dbo.WatchdogLeases
    SET LeaseHolder = @Worker
       ,LeaseEndTime = @LeaseEndTime
       ,LeaseRequestor = ''
       ,@PreviousLeaseHolder = LeaseHolder
    WHERE Watchdog = @Watchdog
      AND NOT (LeaseRequestor <> @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec) -- no valid request to release a lease from others
      AND ( -- lease renew logic
            LeaseHolder = @Worker -- me
              AND (LeaseEndTime > @Now -- still valid
                   OR @WorkerIsRunning = 1)
            OR 
            -- acquire new lease logic
            LeaseEndTime < @Now -- expired
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
    IF @AllowRebalance = 1 
    BEGIN
      SET @CurrentLeaseHolder = (SELECT LeaseHolder FROM dbo.WatchdogLeases WHERE Watchdog = @Watchdog)

      -- refresh request
      UPDATE dbo.WatchdogLeases
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
          FROM dbo.WatchdogLeases A
          WHERE Watchdog = @Watchdog
            AND NOT (LeaseRequestor <> @Worker AND datediff(second,LeaseRequestTime,@Now) < @LeasePeriodSec) -- no valid request to release a lease from others
            AND @NotLeasedWatchdogNumber = 0
            AND (SELECT count(*) FROM dbo.WatchdogLeases B WHERE B.LeaseHolder = A.LeaseHolder AND datediff(second,B.LeaseEndTime,@Now) < @LeasePeriodSec) > @DesiredLeasesNumber -- current guy holds more than required
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
--DECLARE @LeaseEndTime  datetime
--       ,@IsAcquired     bit
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'whatever'
--       ,@Worker = 'box.1234'
--       ,@NumberOfWorkers = 4
--       ,@LeasePeriodSec = 120
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT

--DELETE FROM WatchdogLeases WHERE Id IN ('R1','R2')
--DECLARE @LeaseEndTime  datetime
--       ,@IsAcquired     bit
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R1 by P1', @LeaseEndTime, @IsAcquired
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R2 by P1', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire R1 by P2 while P1 lease is valid', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire R2 by P2', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--WAITFOR DELAY '00:00:02'
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to renew lease on R1 by P1 while P1 lease is valid', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--WAITFOR DELAY '00:00:04'
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire R1 by P2 when P1 lease expired', @LeaseEndTime, @IsAcquired
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to renew R2 by P2', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases
--WAITFOR DELAY '00:00:02'
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P2'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to renew R2 by P2', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R1'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R1 by P1', @LeaseEndTime, @IsAcquired
--EXECUTE AcquireWatchdogLease 
--        @Watchdog = 'R2'
--       ,@Worker = 'P1'
--       ,@NumberOfWorkers = 2
--       ,@LeaseEndTime = @LeaseEndTime OUT
--       ,@IsAcquired  = @IsAcquired OUT
--SELECT 'Try to acquire lease on R2 by P1', @LeaseEndTime, @IsAcquired
--SELECT *, Now = getUTCdate() FROM WatchdogLeases

--SELECT TOP 10 * FROM EventLog WHERE Process = 'AcquireWatchdogLease' ORDER BY EventDate DESC
----SELECT TOP 10 * FROM EventLog ORDER BY EventId DESC
--SELECT count(*) FROM EventLog WHERE Process = 'AcquireWatchdogLease'
--DELETE TOP (100000) FROM EventLog WHERE Process = 'AcquireWatchdogLease'
