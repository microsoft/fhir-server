--DROP PROCEDURE CommitTransaction
GO
CREATE PROCEDURE dbo.CommitTransaction
   @TransactionId bigint
  ,@IsWatchDog bit
  ,@FailureReason varchar(max) = NULL
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+isnull(convert(varchar(50),@TransactionId),'NULL')+' WD='+isnull(convert(varchar,@IsWatchDog),'NULL')+' F='+isnull(convert(varchar,CASE WHEN @FailureReason IS NULL THEN 0 ELSE 1 END),'NULL')
       ,@tmpTransactionId bigint
       ,@Flag bit
       ,@TranCount int
       ,@Rows int = 0
       ,@msg varchar(1000)
       ,@st datetime = getUTCdate()
       ,@IsCompletedBefore bit

BEGIN TRY
  SET @TranCount = @@trancount
  IF @TranCount = 0 BEGIN TRANSACTION

  UPDATE dbo.Transactions
    SET EndDate = getUTCdate()
       ,IsCompleted = 1
       ,@IsCompletedBefore = IsCompleted
       ,IsSuccess = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END
       ,FailureReason = @FailureReason
    WHERE PartitionId = @TransactionId % 8
      AND TransactionId = @TransactionId
      AND (IsControlledByClient = 1 OR @IsWatchDog = 1)
  SET @Rows = @@rowcount

  IF @Rows = 0
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

  IF @TranCount = 0 COMMIT TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @TranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
