--DROP PROCEDURE MergeResourcesAdvanceTransactionVisibility
GO
CREATE PROCEDURE dbo.MergeResourcesAdvanceTransactionVisibility @AffectedRows int = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)
       ,@MaxTransactionId bigint
       ,@MinTransactionId bigint
       ,@MinNotCompletedTransactionId bigint
       ,@CurrentTransactionId bigint

SET @AffectedRows = 0

BEGIN TRY
  EXECUTE dbo.MergeResourcesGetTransactionVisibility @MinTransactionId OUT
  SET @MinTransactionId += 1

  SET @CurrentTransactionId = (SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions ORDER BY SurrogateIdRangeFirstValue DESC)

  SET @MinNotCompletedTransactionId = isnull((SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsCompleted = 0 AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId ORDER BY SurrogateIdRangeFirstValue),@CurrentTransactionId + 1)

  SET @MaxTransactionId = (SELECT TOP 1 SurrogateIdRangeFirstValue FROM dbo.Transactions WHERE IsCompleted = 1 AND SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId AND SurrogateIdRangeFirstValue < @MinNotCompletedTransactionId ORDER BY SurrogateIdRangeFirstValue DESC)

  IF @MaxTransactionId >= @MinTransactionId
  BEGIN
    UPDATE A
      SET IsVisible = 1
         ,VisibleDate = getUTCdate()
      FROM dbo.Transactions A WITH (INDEX = 1)
      WHERE SurrogateIdRangeFirstValue BETWEEN @MinTransactionId AND @CurrentTransactionId 
        AND SurrogateIdRangeFirstValue <= @MaxTransactionId
    SET @AffectedRows += @@rowcount
  END

  SET @msg = 'Min='+convert(varchar,@MinTransactionId)+' C='+convert(varchar,@CurrentTransactionId)+' MinNC='+convert(varchar,@MinNotCompletedTransactionId)+' Max='+convert(varchar,@MaxTransactionId)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
