--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @SurrogateIdRangeFirstValue bigint = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@TransactionId bigint = NULL

BEGIN TRY
  IF @@trancount > 0 RAISERROR('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127)

  SET TRANSACTION ISOLATION LEVEL READ COMMITTED
    
  WHILE @TransactionId IS NULL
  BEGIN
    EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = NULL

    SET @SurrogateIdRangeFirstValue = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + convert(int,@FirstValueVar)

    BEGIN TRANSACTION

    INSERT INTO dbo.Transactions
           (  SurrogateIdRangeFirstValue,                SurrogateIdRangeLastValue )
      SELECT @SurrogateIdRangeFirstValue, @SurrogateIdRangeFirstValue + @Count - 1

    IF isnull((SELECT TOP 1 SurrogateIdRangeLastValue FROM dbo.Transactions WHERE SurrogateIdRangeFirstValue < @SurrogateIdRangeFirstValue ORDER BY SurrogateIdRangeFirstValue DESC),0) < @SurrogateIdRangeFirstValue
    BEGIN
      COMMIT TRANSACTION
      SET @TransactionId = @SurrogateIdRangeFirstValue
    END
    ELSE
    BEGIN
      ROLLBACK TRANSACTION
      SET @TransactionId = NULL
      EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Warn',@Start=@st,@Rows=NULL,@Text=@SurrogateIdRangeFirstValue
    END
  END
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @SurrogateIdRangeFirstValue bigint
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 100, @TransactionId = 0, @SurrogateIdRangeFirstValue = @SurrogateIdRangeFirstValue OUT
--SELECT @SurrogateIdRangeFirstValue
--SELECT TOP 10 * FROM Transactions ORDER BY SurrogateIdRangeFirstValue DESC
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-60,getUTCdate()) AND Process = 'MergeResourcesBeginTransaction' ORDER BY EventDate DESC, EventId DESC
