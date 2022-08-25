--DROP PROCEDURE dbo.DequeueStoreCopyWorkUnit
GO
CREATE PROCEDURE dbo.DequeueStoreCopyWorkUnit
   @StartPartitionId tinyint
  ,@Worker varchar(100)
AS
set nocount on
DECLARE @SP varchar(100) = 'DequeueStoreCopyWorkUnit'
       ,@Mode varchar(100) = 'SP='+isnull(convert(varchar,@StartPartitionId),'NULL')
                           +' W='+isnull(@Worker,'NULL')
       ,@Rows int = 0
       ,@st datetime = getUTCdate()
       ,@ResourceTypeId tinyint
       ,@UnitId int
       ,@msg varchar(100)
       ,@Stop bit = CASE WHEN EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'StoreCopy.Stop' AND Number = 1) THEN 1 ELSE 0 END
       ,@Lock varchar(100)
       ,@PartitionId tinyint = @StartPartitionId
       ,@MaxPartitions tinyint = 16 -- !!! hardcoded
       ,@LookedAtPartitions tinyint = 0
       ,@HeartbeatTimeout int

BEGIN TRY
  SET TRANSACTION ISOLATION LEVEL READ COMMITTED 

  IF @Stop = 0
  BEGIN
    WHILE @UnitId IS NULL AND @LookedAtPartitions <= @MaxPartitions
    BEGIN
      SET @Lock = 'DequeueStoreCopyWorkUnit_'+convert(varchar, @PartitionId)

      BEGIN TRANSACTION  

      EXECUTE sp_getapplock @Lock, 'Exclusive'

      UPDATE T
        SET StartDate = getUTCdate()
           ,HeartBeatDate = getUTCdate()
           ,Worker = @Worker 
           ,Status = 1 -- running
           ,@ResourceTypeId = T.ResourceTypeId
           ,@UnitId = T.UnitId
        FROM dbo.StoreCopyWorkQueue T WITH (PAGLOCK)
             JOIN (SELECT TOP 1 
                          UnitId
                     FROM dbo.StoreCopyWorkQueue WITH (INDEX = IX_Status_PartitionId)
                     WHERE PartitionId = @PartitionId
                       AND Status = 0
                     ORDER BY 
                          UnitId
                  ) S
               ON PartitionId = @PartitionId AND T.UnitId = S.UnitId
      SET @Rows = @@rowcount

      COMMIT TRANSACTION

      IF @UnitId IS NULL
      BEGIN
        SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
        SET @LookedAtPartitions = @LookedAtPartitions + 1 
      END
    END

    -- Do timed out items. Logic is not looking at heartbeat time, because this logic is not plugged in 
    SET @HeartbeatTimeout = isnull((SELECT Number FROM dbo.Parameters WHERE Id = 'StoreCopy.HeartbeatTimeoutSec'),3600)
    SET @LookedAtPartitions = 0
    WHILE @UnitId IS NULL AND @LookedAtPartitions <= @MaxPartitions
    BEGIN
      SET @Lock = 'DequeueStoreCopyWorkUnit_'+convert(varchar, @PartitionId)

      BEGIN TRANSACTION  

      EXECUTE sp_getapplock @Lock, 'Exclusive'

      UPDATE T
        SET StartDate = getUTCdate()
           ,HeartBeatDate = getUTCdate()
           ,Worker = @Worker 
           ,Status = 1 -- running
           ,@ResourceTypeId = T.ResourceTypeId
           ,@UnitId = T.UnitId
           ,Info = isnull(Info,'')+' Prev: Worker='+Worker+' Start='+convert(varchar,StartDate,121)
        FROM dbo.StoreCopyWorkQueue T WITH (PAGLOCK)
             JOIN (SELECT TOP 1 
                          UnitId
                     FROM dbo.StoreCopyWorkQueue WITH (INDEX = IX_Status_PartitionId)
                     WHERE PartitionId = @PartitionId
                       AND Status = 1
                       AND datediff(second,StartDate,getUTCdate()) > @HeartbeatTimeout
                     ORDER BY 
                          UnitId
                  ) S
               ON PartitionId = @PartitionId AND T.UnitId = S.UnitId
      SET @Rows = @@rowcount

      COMMIT TRANSACTION

      IF @UnitId IS NULL
      BEGIN
        SET @PartitionId = CASE WHEN @PartitionId = 15 THEN 0 ELSE @PartitionId + 1 END
        SET @LookedAtPartitions = @LookedAtPartitions + 1 
      END
    END
  END

  IF @UnitId IS NOT NULL
    SELECT PartitionId
          ,ResourceTypeId
          ,UnitId
          ,MinIdOrUrl
          ,MaxId
          ,ResourceCount
      FROM dbo.StoreCopyWorkQueue
      WHERE PartitionId = @PartitionId AND UnitId = @UnitId 
  
  SET @msg = 'P='+convert(varchar,@PartitionId)+' U='+isnull(convert(varchar,@UnitId),'NULL')+' RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')+' S='+convert(varchar,@Stop)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
