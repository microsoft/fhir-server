--DROP PROCEDURE MergeResourcesGetTimeoutTransactions
GO
CREATE PROCEDURE dbo.MergeResourcesGetTimeoutTransactions @TimeoutSec int
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TimeoutSec)
       ,@st datetime = getUTCdate()
       ,@MinTransactionId bigint

BEGIN TRY
  EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUT

  SELECT SurrogateIdRangeFirstValue
    FROM dbo.Transactions 
    WHERE SurrogateIdRangeFirstValue > @MinTransactionId
      AND datediff(second, HeartbeatDate, getUTCdate()) > @TimeoutSec
    ORDER BY SurrogateIdRangeFirstValue

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
