--DROP PROCEDURE dbo.GetTransactionById
GO
CREATE PROCEDURE dbo.GetTransactionById @TransactionId bigint
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+isnull(convert(varchar,@TransactionId),'NULL')
       ,@st datetime = getUTCdate()

SELECT TransactionId
      ,IsCompleted
      ,IsSuccess
      ,IsVisible
      ,IsHistoryMoved
      ,CreateDate
      ,EndDate
      ,VisibleDate
      ,HistoryMovedDate
      ,HeartbeatDate
      ,FailureReason
      ,IsControlledByClient
  FROM dbo.Transactions
  WHERE TransactionId = @TransactionId

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
GO
-- Quick test code.
-- SELECT * FROM ChangeType 
--EXECUTE GetTransactionById @TransactionId=-3
