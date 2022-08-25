---DROP PROCEDURE dbo.GetVisibilityBoundaryTransactionId
GO
CREATE PROCEDURE dbo.GetVisibilityBoundaryTransactionId @TransactionId bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@MaxTransactionId bigint
       ,@PartitionId tinyint

SET @TransactionId = -1
SET @PartitionId = 0
WHILE @PartitionId < 8
BEGIN
  SET @MaxTransactionId = (SELECT TOP 1 TransactionId FROM dbo.Transactions WHERE PartitionId = @PartitionId AND IsVisible = 1 ORDER BY TransactionId DESC)
  IF @MaxTransactionId > @TransactionId
    SET @TransactionId = @MaxTransactionId
  SET @PartitionId += 1
END

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@TransactionId
GO
--set statistics io on
--set statistics time on
--DECLARE @TransactionId bigint
--EXECUTE GetVisibilityBoundaryTransactionId @TransactionId OUT
--SELECT @TransactionId
