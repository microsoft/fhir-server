--DROP PROCEDURE dbo.MergeResourcesCommitTransaction
GO
CREATE PROCEDURE dbo.MergeResourcesCommitTransaction @SurrogateIdRangeFirstValue bigint, @FailureReason varchar(max) = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesCommitTransaction'
       ,@Mode varchar(200) = 'TR='+convert(varchar,@SurrogateIdRangeFirstValue)
       ,@st datetime = getUTCdate()

BEGIN TRY
  UPDATE dbo.Transactions 
    SET IsCompleted = 1
       ,IsSuccess = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END
       ,EndDate = getUTCdate()
       ,IsVisible = 1 -- this will change in future
       ,VisibleDate = getUTCdate()
       ,FailureReason = @FailureReason
    WHERE SurrogateIdRangeFirstValue = @SurrogateIdRangeFirstValue
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
