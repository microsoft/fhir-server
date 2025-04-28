ALTER PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @TransactionId bigint OUT, @SequenceRangeFirstValue int = NULL OUT, @HeartbeatDate datetime = NULL, @EnableThrottling bit = 0
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)+' HB='+isnull(convert(varchar,@HeartbeatDate,121),'NULL')+' ET='+convert(varchar,@EnableThrottling)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@LastValueVar sql_variant
       -- Optimal concurrency slowly increases with transaction size growth. For single resource transactions optimal value is ~64.
       -- For large transactions (1000 resources) typical for $import it is ~256. 
       -- Transaction size is hard to get in efficient way, so I choose 256 as default.
       ,@OptimalConcurrency int = isnull((SELECT Number FROM Parameters WHERE Id = 'MergeResources.OptimalConcurrentCalls'), 256)
       ,@CurrentConcurrency int
       ,@msg varchar(1000)

BEGIN TRY
  SET @TransactionId = NULL

  IF @@trancount > 0 RAISERROR('MergeResourcesBeginTransaction cannot be called inside outer transaction.', 18, 127)

  IF @EnableThrottling = 1
  BEGIN
    SET @CurrentConcurrency = (SELECT count(*) FROM sys.dm_exec_sessions WHERE status <> 'sleeping' AND program_name = 'MergeResources')
    IF @CurrentConcurrency > @OptimalConcurrency
    BEGIN
      SET @msg = 'Number of concurrent MergeResources calls = '+convert(varchar,@CurrentConcurrency)+' is above optimal = '+convert(varchar,@OptimalConcurrency)+'.';
      THROW 50410, @msg, 1 
    END
  END 

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
