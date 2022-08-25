CREATE PROCEDURE dbo.CheckTransactionHeartbeat @TransactionId bigint 
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+isnull(convert(varchar,@TransactionId),'NULL')
       ,@Rows int = 0
       ,@st datetime = getUTCdate()

IF @TransactionId IS NULL
  SELECT TransactionId
        ,HeartbeatDate
        ,IsControlledByClient
        ,IsCompleted
    FROM dbo.Transactions
    WHERE IsCompleted = 0
ELSE
  SELECT TransactionId
        ,HeartbeatDate
        ,IsControlledByClient
        ,IsCompleted
    FROM dbo.Transactions
    WHERE TransactionId = @TransactionId

SET @Rows = @Rows + @@rowcount
EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows
GO
