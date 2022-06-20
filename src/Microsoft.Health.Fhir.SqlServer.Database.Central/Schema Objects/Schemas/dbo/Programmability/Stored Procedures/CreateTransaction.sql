--DROP PROCEDURE dbo.CreateTransaction
GO
CREATE PROCEDURE dbo.CreateTransaction
   @TransactionId               bigint = NULL OUT 
  ,@HeartbeatDate               datetime = NULL
  ,@TimeoutSeconds              int = 600
  ,@ReturnRecordSet             bit = 1
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'H='+isnull(convert(varchar,@HeartbeatDate,127),'NULL')
                           +' T='+isnull(convert(varchar,@TimeoutSeconds),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  BEGIN TRANSACTION

  EXECUTE sp_getapplock 'CreateTransaction', 'Exclusive'
  
  SET @TransactionId = isnull((SELECT TOP 1 TransactionId FROM dbo.Transactions ORDER BY TransactionId DESC) + 1, 0)

  INSERT INTO dbo.Transactions 
         (  TransactionId,                      HeartbeatDate)
    SELECT @TransactionId, isnull(@HeartbeatDate,getUTCdate())

  COMMIT TRANSACTION

  IF @ReturnRecordSet = 1 EXECUTE dbo.GetTransactionById @TransactionId
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
-- Quick test code.
--EXECUTE CreateTransaction @IsPending=1,@ChangeTypeId=3, @BranchId=0,@UserId=0,@Metadata='<a></a>',@Description='Test',@BaseTransactionId=0
--SELECT * FROM Transaction
--EXECUTE StartCommitTransaction @TransactionId=-1
