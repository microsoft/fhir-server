--DROP PROCEDURE dbo.MergeResourcesPutTransactionInvisibleHistory
GO
CREATE PROCEDURE dbo.MergeResourcesPutTransactionInvisibleHistory @TransactionId bigint
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100)= 'TR='+convert(varchar,@TransactionId)
       ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.Transactions
    SET InvisibleHistoryRemovedDate = getUTCdate()
    WHERE SurrogateIdRangeFirstValue = @TransactionId
      AND InvisibleHistoryRemovedDate IS NULL

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
