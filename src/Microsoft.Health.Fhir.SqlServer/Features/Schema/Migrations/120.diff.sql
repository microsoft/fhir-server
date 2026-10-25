IF col_length('dbo.WatchdogLeases', 'LastRunDate') IS NULL
  ALTER TABLE dbo.WatchdogLeases ADD LastRunDate datetime NOT NULL CONSTRAINT DF_WatchdogLeases_LastRunDate DEFAULT 0
GO
CREATE OR ALTER PROCEDURE dbo.GetWatchdogLeasesLastRunDate
   @Watchdog varchar(100)
  ,@LastRunDate datetime OUT
AS
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(200) = 'WD='+isnull(@Watchdog,'NULL')
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)

BEGIN TRY
  SET @LastRunDate = (SELECT LastRunDate FROM dbo.WatchdogLeases WHERE Watchdog = @Watchdog)
  SET @msg = 'LRD='+isnull(convert(varchar(23),@LastRunDate,126),'NULL')

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@msg
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
CREATE OR ALTER PROCEDURE dbo.UpdateWatchdogLeasesLastRunDate
   @Watchdog varchar(100)
  ,@LastRunDate datetime
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
        ,@Mode varchar(200) = 'WD='+isnull(@Watchdog,'NULL')+' LRD='+isnull(convert(varchar(23),@LastRunDate,126),'NULL')
        ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.WatchdogLeases SET LastRunDate = @LastRunDate WHERE Watchdog = @Watchdog

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
ALTER PROCEDURE dbo.AcquireWatchdogLease
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
    UPDATE dbo.WatchdogLeases
      SET LeaseHolder = ''
         ,LeaseEndTime = dateadd(day,-10,@Now)
         ,LeaseRequestor = ''
         ,LeaseRequestTime = dateadd(day,-10,@Now)
      WHERE Watchdog IN (SELECT Watchdog FROM @Watchdogs)
    SET @Rows += @@rowcount

    IF @Rows > 0
    BEGIN
      SET @msg = ''
      SELECT @msg = convert(varchar(1000),@msg + CASE WHEN @msg = '' THEN '' ELSE ',' END + Watchdog) FROM @Watchdogs
      SET @msg = convert(varchar(1000),'Reset old/forced leases:'+@msg)
      EXECUTE dbo.LogEvent @Process='AcquireWatchdogLease',@Status='Info',@Mode=@Mode,@Target='WatchdogLeases',@Action='Update',@Rows=@Rows,@Text=@msg
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
