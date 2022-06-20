--DROP PROCEDURE CompleteTransactionLoad
GO
CREATE PROCEDURE dbo.CompleteTransaction
   @TransactionId bigint
  ,@FailureReason xml = NULL
  ,@IsWatchDog bit
  ,@IsFailed bit = NULL
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+isnull(convert(varchar(50),@TransactionId),'NULL')+' WD='+isnull(convert(varchar,@IsWatchDog),'NULL')+' F='+isnull(convert(varchar,@IsFailed),'NULL')
       ,@tmpTransactionId bigint
       ,@Flag bit
       ,@TranCount int
       ,@Rows int = 0
       ,@RowsTmp int
       ,@msg varchar(1000)
       ,@st datetime = getUTCdate()
       ,@IsCompletedBefore bit

BEGIN TRY
  SET @TranCount = @@trancount
  IF @TranCount = 0 BEGIN TRANSACTION

  -- Move boundary logic relies on locking on rows being written
  -- This app lock is required to fight against SQL Azure snapshot isolation
  -- READCOMMITTED locking hints and setting READ COMMITTED transaction isolation level did not help.
  EXECUTE sp_getapplock 'CompleteTransaction', 'Exclusive'

  UPDATE dbo.Transactions
    SET EndDate = getUTCdate()
       ,IsCompleted = 1
       ,@IsCompletedBefore = IsCompleted
       ,IsSuccess = CASE WHEN isnull(@IsFailed, CASE WHEN @FailureReason IS NOT NULL THEN 1 ELSE 0 END) = 1 THEN 0 ELSE 1 END
       ,FailureReason = isnull(@FailureReason, FailureReason) -- There could be a case when failure reason is not null but Transaction is success
    WHERE TransactionId = @TransactionId
      AND (IsControlledByClient = 1 OR @IsWatchDog = 1)
  SET @RowsTmp = @@rowcount
  SET @Rows += @RowsTmp

  IF @RowsTmp = 0
  BEGIN
    SET @msg = 'Transaction ['+convert(varchar(20),@TransactionId)+'] is not controlled by client or does not exist.'
    RAISERROR(@msg, 18, 127)
  END

  IF @IsCompletedBefore = 1
  BEGIN
    -- To make this call idempotent
    ROLLBACK TRANSACTION
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Target='@IsCompletedBefore',@Text='=1'
    RETURN
  END

  EXECUTE dbo.CompleteTransactionAdvanceVisibility @TransactionId = @TransactionId

  IF @TranCount = 0 COMMIT TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @TranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
-- Test code
--DELETE FROM Transaction WHERE TransactionId > 1
--EXECUTE CreateTransaction 0,3,0,0 -- creates 2
--EXECUTE CreateTransaction 0,3,0,0 -- creates 3
--EXECUTE CreateTransaction 0,3,0,0 -- creates 4
--EXECUTE CreateTransaction 0,3,0,0 -- creates 5
--EXECUTE CompleteTransaction 4 -- completes 4, boundary stays on 1
--EXECUTE CompleteTransaction 3 -- completes 3, boundary stays on 1
--EXECUTE CompleteTransaction 2 -- completes 2, boundary moved on 4
--SELECT * FROM Transaction
