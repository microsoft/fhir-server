--DROP PROCEDURE AdvanceTransactionVisibility
GO
CREATE PROCEDURE dbo.AdvanceTransactionVisibility @AffectedRows int = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)
       ,@TransactionId bigint
       ,@MaxTransactionId bigint
       ,@MinTransactionId bigint
       ,@MinNotCompletedTransactionId bigint
       ,@Partitionid tinyint
       ,@CurrentSequence bigint
       ,@Count int
       ,@ExpectedCount int
       ,@Waits int = 0

SET @AffectedRows = 0

BEGIN TRY
  EXECUTE dbo.GetVisibilityBoundaryTransactionId @MinTransactionId OUT
  SET @MinTransactionId += 1

  SET @CurrentSequence = (SELECT convert(bigint, current_value) FROM sys.sequences WHERE object_id = object_id('TransactionIdSequence'))

  SET @ExpectedCount = @CurrentSequence - @MinTransactionId + 1
  IF @ExpectedCount < 0
    RAISERROR('@ExpectedCount < 0', 18, 127)

  WAITFOR DELAY '00:00:00.100' -- wait to increase probability of inserts to finish

  -- wait till all inserts finish
  SET @Count = 0
  WHILE @Count < @ExpectedCount AND @Waits < 10 -- If dust does not settle in 10 waits assume permanent transaction id gap.
  BEGIN
    SET @Count = 0
    SET @PartitionId = 0
    WHILE @PartitionId < 8
    BEGIN
      SET @Count += (SELECT count(*) FROM dbo.Transactions WITH (INDEX = 1) WHERE PartitionId = @PartitionId AND TransactionId BETWEEN @MinTransactionId AND @CurrentSequence)
      SET @PartitionId += 1
    END
    IF @Count < @ExpectedCount
    BEGIN
      SET @Waits += 1
      WAITFOR DELAY '00:00:00.100'
    END
  END

  SET @MinNotCompletedTransactionId = 9223372036854775807
  SET @PartitionId = 0
  WHILE @PartitionId < 8
  BEGIN
    SET @TransactionId = (SELECT TOP 1 TransactionId FROM dbo.Transactions WHERE PartitionId = @PartitionId AND IsCompleted = 0 AND TransactionId BETWEEN @MinTransactionId AND @CurrentSequence ORDER BY TransactionId)
    IF @MinNotCompletedTransactionId > @TransactionId
      SET @MinNotCompletedTransactionId = @TransactionId
    SET @PartitionId += 1
  END

  SET @MaxTransactionId = -1
  SET @Partitionid = 0
  WHILE @Partitionid < 8
  BEGIN
    SET @TransactionId = (SELECT TOP 1 TransactionId FROM dbo.Transactions WHERE PartitionId = @PartitionId AND IsCompleted = 1 AND TransactionId BETWEEN @MinTransactionId AND @CurrentSequence AND TransactionId < @MinNotCompletedTransactionId ORDER BY TransactionId DESC)
    IF @MaxTransactionId < @TransactionId
      SET @MaxTransactionId = @TransactionId
    SET @PartitionId += 1
  END

  BEGIN TRANSACTION

  IF @MaxTransactionId >= @MinTransactionId
  BEGIN
    SET @Partitionid = 0
    WHILE @Partitionid < 8
    BEGIN
      UPDATE A
        SET IsVisible = 1
           ,VisibleDate = getUTCdate()
        FROM dbo.Transactions A WITH (INDEX = 1)
        WHERE PartitionId = @PartitionId
          AND TransactionId BETWEEN @MinTransactionId AND @CurrentSequence 
          AND TransactionId <= @MaxTransactionId
      SET @AffectedRows += @@rowcount

      SET @PartitionId += 1
    END
  END

  COMMIT TRANSACTION

  SET @msg = 'Min='+convert(varchar,@MinTransactionId)+' CS='+convert(varchar,@CurrentSequence)+' MinNC='+convert(varchar,@MinNotCompletedTransactionId)+' Max='+convert(varchar,@MaxTransactionId)+' Waits='+convert(varchar,@Waits)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@msg
END TRY
BEGIN CATCH
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
