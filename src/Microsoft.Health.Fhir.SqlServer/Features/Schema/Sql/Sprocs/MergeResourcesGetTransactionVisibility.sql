--DROP PROCEDURE dbo.MergeResourcesGetTransactionVisibility
GO
CREATE PROCEDURE dbo.MergeResourcesGetTransactionVisibility @TransactionId bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()

SET @TransactionId = isnull((SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsVisible = 1 ORDER BY SurrogateIdRangeFirstValue DESC),-1)

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount,@Text=@TransactionId
GO
--DECLARE @TransactionId bigint
--EXECUTE MergeResourcesGetTransactionVisibility @TransactionId OUT
--SELECT @TransactionId
