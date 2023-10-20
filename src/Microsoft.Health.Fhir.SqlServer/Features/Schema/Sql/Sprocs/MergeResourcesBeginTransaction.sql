﻿--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @TransactionId bigint OUT, @SequenceRangeFirstValue int = NULL OUT, @HeartbeatDate datetime = NULL
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@LastValueVar sql_variant

BEGIN TRY
  SET @TransactionId = NULL

  IF @@trancount > 0 RAISERROR('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127)
 
  SET @FirstValueVar = NULL
  WHILE @FirstValueVar IS NULL
  BEGIN
    EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = @LastValueVar OUT
    SET @SequenceRangeFirstValue = convert(int,@FirstValueVar)
    IF @SequenceRangeFirstValue > convert(int,@LastValueVar)
      SET @FirstValueVar = NULL
  END

  SET @TransactionId = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue

  INSERT INTO dbo.Transactions
         (  SurrogateIdRangeFirstValue,   SurrogateIdRangeLastValue,                      HeartbeatDate )
    SELECT              @TransactionId, @TransactionId + @Count - 1, isnull(@HeartbeatDate,getUTCdate() )
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @TransactionId bigint
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 100, @TransactionId = @TransactionId OUT
--SELECT @TransactionId
--SELECT TOP 10 * FROM Transactions ORDER BY SurrogateIdRangeFirstValue DESC
--SELECT TOP 100 * FROM EventLog WHERE EventDate > dateadd(minute,-60,getUTCdate()) AND Process = 'MergeResourcesBeginTransaction' ORDER BY EventDate DESC, EventId DESC
