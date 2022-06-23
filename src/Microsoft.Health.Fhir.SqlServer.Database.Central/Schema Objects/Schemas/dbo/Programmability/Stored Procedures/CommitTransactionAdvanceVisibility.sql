--DROP PROCEDURE CommitTransactionAdvanceVisibility
GO
CREATE PROCEDURE dbo.CommitTransactionAdvanceVisibility @TransactionId bigint
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+isnull(convert(varchar(50),@TransactionId),'NULL')
       ,@tmpTransactionId bigint
       ,@Flag bit
       ,@Rows int = 0
       ,@RowsTmp int
       ,@st datetime = getUTCdate()
       ,@TranCount int = @@trancount

BEGIN TRY
  -- Move boundary in separate transaction to minimize rollbacks of change sets in case of deadlocks
  IF @TransactionId IS NULL RAISERROR('@TransactionId cannot be null', 18, 127)

  -- Check left
  SET @tmpTransactionId = NULL
  SELECT TOP 1 @tmpTransactionId = TransactionId, @Flag = IsVisible FROM dbo.Transactions WHERE TransactionId < @TransactionId ORDER BY TransactionId DESC

  UPDATE dbo.Transactions 
    SET IsVisible = 1
       ,VisibleDate = getUTCdate()
    WHERE TransactionId = @TransactionId
      AND (@tmpTransactionId IS NULL -- no left
           OR @tmpTransactionId < 0 -- no normal left
           OR @Flag = 1 -- left is boundary
          )
      AND IsVisible = 0 -- This should allow reprocessing of change sets by replicator without re-updating a lot
  SET @RowsTmp = @@rowcount
  SET @Rows = @Rows + @RowsTmp

  IF @RowsTmp > 0
  BEGIN
    -- Check right
    SET @Flag = NULL
    SELECT TOP 1 @tmpTransactionId = TransactionId, @Flag = IsCompleted FROM dbo.Transactions WHERE TransactionId > @TransactionId ORDER BY TransactionId

    WHILE @Flag = 1 -- if right is completed
    BEGIN
      UPDATE dbo.Transactions 
        SET IsVisible = 1
           ,VisibleDate = getUTCdate()
        WHERE TransactionId = @tmpTransactionId
      SET @Rows = @Rows + @@rowcount

      SET @TransactionId = @tmpTransactionId
      
      SET @Flag = NULL
      SELECT TOP 1 @tmpTransactionId = TransactionId, @Flag = IsCompleted FROM dbo.Transactions WHERE TransactionId > @TransactionId ORDER BY TransactionId
    END
  END

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
END TRY
BEGIN CATCH
  IF @TranCount = 0 AND @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
