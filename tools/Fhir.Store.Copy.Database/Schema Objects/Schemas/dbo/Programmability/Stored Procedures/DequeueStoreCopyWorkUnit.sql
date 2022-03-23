--DROP PROCEDURE dbo.DequeueStoreCopyWorkUnit
GO
CREATE PROCEDURE dbo.DequeueStoreCopyWorkUnit
   @Thread tinyint -- thread separation is turned off for now
  ,@Worker varchar(100)
AS
set nocount on
DECLARE @SP varchar(100) = 'DequeueStoreCopyWorkUnit'
       ,@Mode varchar(100) = 'W='+isnull(@Worker,'NULL')
                           +' T='+isnull(convert(varchar,@Thread),'NULL')
       ,@Rows int = 0
       ,@st datetime = getUTCdate()
       ,@UnitId int
       ,@ResourceTypeId smallint
       ,@msg varchar(100)
       ,@Stop bit = CASE WHEN EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'StoreCopy.Stop' AND Number = 1) THEN 1 ELSE 0 END
       ,@PartitionId tinyint = 16 * rand()
       ,@Lock varchar(100)

BEGIN TRY
  SET @Lock = 'DequeueStoreCopyWorkUnit_'+convert(varchar, @PartitionId)

  IF @Stop = 0
  BEGIN
    SET TRANSACTION ISOLATION LEVEL READ COMMITTED 

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
  END

  IF @UnitId IS NOT NULL
    SELECT ResourceTypeId
          ,UnitId
          ,MinId
          ,MaxId
          ,ResourceCount
      FROM dbo.StoreCopyWorkQueue
      WHERE UnitId = @UnitId
  
  SET @msg = 'P='+convert(varchar,@PartitionId)+' U='+isnull(convert(varchar,@UnitId),'NULL')+' RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')+' S='+convert(varchar,@Stop)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
