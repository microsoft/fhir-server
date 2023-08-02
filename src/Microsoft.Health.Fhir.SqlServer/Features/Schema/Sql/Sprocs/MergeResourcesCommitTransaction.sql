CREATE PROCEDURE dbo.MergeResourcesCommitTransaction @TransactionId bigint = NULL, @FailureReason varchar(max) = NULL, @OverrideIsControlledByClientCheck bit = 0, @SurrogateIdRangeFirstValue bigint = NULL -- TODO: Remove after deployment
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesCommitTransaction'
       ,@st datetime = getUTCdate()
       ,@InitialTranCount int = @@trancount
       ,@IsCompletedBefore bit
       ,@Rows int
       ,@msg varchar(1000)

SET @TransactionId = isnull(@TransactionId,@SurrogateIdRangeFirstValue)

DECLARE @Mode varchar(200) = 'TR='+convert(varchar,@TransactionId)+' OC='+isnull(convert(varchar,@OverrideIsControlledByClientCheck),'NULL')

BEGIN TRY
  IF @InitialTranCount = 0 BEGIN TRANSACTION

  UPDATE dbo.Transactions
    SET IsCompleted = 1
       ,@IsCompletedBefore = IsCompleted
       ,EndDate = getUTCdate()
       ,IsSuccess = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END
       ,FailureReason = @FailureReason
    WHERE SurrogateIdRangeFirstValue = @TransactionId
      AND (IsControlledByClient = 1 OR @OverrideIsControlledByClientCheck = 1)
  SET @Rows = @@rowcount

  IF @Rows = 0
  BEGIN
    SET @msg = 'Transaction ['+convert(varchar(20),@TransactionId)+'] is not controlled by client or does not exist.'
    RAISERROR(@msg, 18, 127)
  END

  IF @IsCompletedBefore = 1
  BEGIN
    -- To make this call idempotent
    IF @InitialTranCount = 0 ROLLBACK TRANSACTION
    EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Target='@IsCompletedBefore',@Text='=1'
    RETURN
  END

  IF @InitialTranCount = 0 COMMIT TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @InitialTranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
