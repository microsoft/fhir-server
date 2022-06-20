--DROP PROCEDURE dbo.GetVisibilityBoundaryTransactionId
GO
CREATE PROCEDURE dbo.GetVisibilityBoundaryTransactionId @TransactionId bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()

SET @TransactionId = isnull((SELECT TOP 1 TransactionId FROM dbo.Transactions WHERE IsVisible = 1 ORDER BY TransactionId DESC),0)

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@TransactionId
GO
-- Quick test code.
--EXECUTE GetCompletedBoundaryTransactionId @BranchId=0
