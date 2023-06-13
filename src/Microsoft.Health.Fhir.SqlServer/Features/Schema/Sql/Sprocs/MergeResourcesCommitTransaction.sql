--DROP PROCEDURE dbo.MergeResourcesCommitTransaction
GO
CREATE PROCEDURE dbo.MergeResourcesCommitTransaction @TransactionId bigint = NULL, @FailureReason varchar(max) = NULL, @OverrideIsControlledByClientCheck bit = 0, @SurrogateIdRangeFirstValue bigint = NULL -- TODO: Remove after deployment
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesCommitTransaction'
       ,@st datetime = getUTCdate()

SET @TransactionId = isnull(@TransactionId,@SurrogateIdRangeFirstValue)

DECLARE @Mode varchar(200) = 'TR='+convert(varchar,@TransactionId)

BEGIN TRY
  UPDATE dbo.Transactions 
    SET IsCompleted = 1
       ,IsSuccess = CASE WHEN @FailureReason IS NULL THEN 1 ELSE 0 END
       ,EndDate = getUTCdate()
       ,IsVisible = 1 -- this will change in future
       ,VisibleDate = getUTCdate()
       ,FailureReason = @FailureReason
    WHERE SurrogateIdRangeFirstValue = @TransactionId
      AND (IsControlledByClient = 1 OR @OverrideIsControlledByClientCheck = 1)
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
