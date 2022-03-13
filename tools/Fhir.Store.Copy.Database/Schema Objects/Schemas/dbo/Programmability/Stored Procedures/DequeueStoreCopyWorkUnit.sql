--DROP PROCEDURE dbo.DequeueStoreCopyWorkUnit
GO
CREATE PROCEDURE dbo.DequeueStoreCopyWorkUnit
   @Thread tinyint
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

BEGIN TRY
  BEGIN TRANSACTION  

  EXECUTE sp_getapplock 'DequeueStoreCopyWorkUnit', 'Exclusive'

  UPDATE T
    SET StartDate = getUTCdate()
       ,HeartBeatDate = getUTCdate()
       ,Worker = @Worker 
       ,Status = 1 -- running
       ,@ResourceTypeId = T.ResourceTypeId
       ,@UnitId = T.UnitId
    FROM dbo.StoreCopyWorkQueue T WITH (PAGLOCK)
         JOIN (SELECT TOP 1 
                      ResourceTypeId
                     ,UnitId
                 FROM dbo.StoreCopyWorkQueue WITH (INDEX = IX_Thread_Status)
                 WHERE Thread = @Thread
                   AND Status = 0
                 ORDER BY 
                      ResourceTypeId
                     ,UnitId
              ) S -- TODO: need to sort so that the client with less pending work can get more.
           ON T.ResourceTypeId = S.ResourceTypeId AND T.UnitId = S.UnitId 
  SET @Rows = @@rowcount

  COMMIT TRANSACTION

  IF @UnitId IS NOT NULL
    SELECT ResourceTypeId
          ,UnitId
          ,MinResourceSurrogateId
          ,MaxResourceSurrogateId
          ,ResourceCount
      FROM dbo.StoreCopyWorkQueue
      WHERE ResourceTypeId = @ResourceTypeId
        AND UnitId = @UnitId
  
  SET @msg = 'RT='+isnull(convert(varchar,@ResourceTypeId),'NULL')+' U='+isnull(convert(varchar,@UnitId),'NULL')

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
