--DROP PROCEDURE dbo.GetTransactions
GO
CREATE PROCEDURE dbo.GetTransactions @StartNotInclusiveTranId bigint, @EndInclusiveTranId bigint, @EndDate datetime = NULL
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'ST='+convert(varchar,@StartNotInclusiveTranId)+' ET='+convert(varchar,@EndInclusiveTranId)+' ED='+isnull(convert(varchar,@EndDate,121),'NULL')
       ,@st datetime = getUTCdate()

IF @EndDate IS NULL
  SET @EndDate = getUTCdate()

SELECT SurrogateIdRangeFirstValue
      ,VisibleDate
      ,InvisibleHistoryRemovedDate
  FROM dbo.Transactions 
  WHERE SurrogateIdRangeFirstValue > @StartNotInclusiveTranId
    AND SurrogateIdRangeFirstValue <= @EndInclusiveTranId
    AND EndDate <= @EndDate
  ORDER BY SurrogateIdRangeFirstValue

EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
GO
--SELECT TOP 100 * FROM Transactions ORDER BY SurrogateIdRangeFirstValue DESC
--EXECUTE GetTransactions 5105975696064002770, 5105975696807769789
