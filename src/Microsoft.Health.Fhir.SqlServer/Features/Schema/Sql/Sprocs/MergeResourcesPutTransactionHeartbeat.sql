--DROP PROCEDURE dbo.MergeResourcesPutTransactionHeartbeat
GO
CREATE PROCEDURE dbo.MergeResourcesPutTransactionHeartbeat @TransactionId bigint
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesPutTransactionHeartbeat'
       ,@Mode varchar(100)
       ,@st datetime = getUTCdate()
       ,@Rows int = 0
       ,@PartitionId tinyint = @JobId % 16

SET @Mode = 'TR='+convert(varchar,@TransactionId)

BEGIN TRY
  UPDATE dbo.Transactions
    SET HeartbeatDate = getUTCdate()
    WHERE SurrogateIdRangeFirstValue = @TransactionId
      AND IsControlledByClient = 1
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
