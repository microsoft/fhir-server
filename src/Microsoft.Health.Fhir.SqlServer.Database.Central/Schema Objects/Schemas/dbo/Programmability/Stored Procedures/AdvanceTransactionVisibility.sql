----DROP PROCEDURE AdvanceTransactionVisibility
--GO
--CREATE PROCEDURE dbo.AdvanceTransactionVisibility @AffectedRows int = 0 OUT
--AS
--set nocount on
--DECLARE @SP varchar(100) = object_name(@@procid)
--       ,@Mode varchar(100) = ''
--       ,@TransactionId bigint
--       ,@st datetime = getUTCdate()
--       ,@msg varchar(1000)
--       ,@IsCompleted bit
--       ,@TransactionIds BigintList
--       ,@TransactionRows int = 0

--SET @AffectedRows = 0

--BEGIN TRY
--  -- Get first not visible but completed
--  SET @TransactionId = (SELECT TOP 1 TransactionId FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 0 ORDER BY TransactionId)
  
--  IF @TransactionId IS NOT NULL
--  BEGIN
--    UPDATE dbo.Transactions 
--      SET IsVisible = 1
--         ,VisibleDate = getUTCdate()
--      WHERE TransactionId = @TransactionId
--    SET @AffectedRows += @@rowcount

--    SET @IsCompleted = 1
--    WHILE @IsCompleted = 1
--    BEGIN
--      SET @IsCompleted = NULL
--      SELECT TOP 1 @TransactionId = TransactionId, @IsCompleted = IsCompleted FROM dbo.Transactions WHERE TransactionId > @TransactionId ORDER BY TransactionId

--      IF @IsCompleted = 1
--      BEGIN
--        INSERT INTO @TransactionIds SELECT @TransactionId
--        SET @TransactionRows = @TransactionRows + 1
--      END
--    END

--    IF @TransactionRows > 0
--    BEGIN
--      UPDATE dbo.Transactions 
--        SET IsVisible = 1
--           ,VisibleDate = getUTCdate()
--        WHERE TransactionId IN (SELECT Id FROM @TransactionIds)
--      SET @AffectedRows += @@rowcount
--    END
--  END

--  SET @msg = 'ST='+convert(varchar,@TransactionId)
--  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@msg
--END TRY
--BEGIN CATCH
--  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
--  THROW
--END CATCH
--GO
--DROP PROCEDURE AdvanceTransactionVisibility
GO
CREATE PROCEDURE dbo.AdvanceTransactionVisibility @AffectedRows int = 0 OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = ''
       ,@TransactionId bigint
       ,@st datetime = getUTCdate()
       ,@msg varchar(1000)
       ,@IsCompleted bit
       ,@MaxTransactionId bigint
       ,@MinTransactionId bigint

SET @AffectedRows = 0

BEGIN TRY
  -- Get first not visible but completed
  SET @TransactionId = (SELECT TOP 1 TransactionId FROM dbo.Transactions WHERE IsCompleted = 1 AND IsVisible = 0 ORDER BY TransactionId)
  
  IF @TransactionId IS NOT NULL
  BEGIN
    UPDATE dbo.Transactions 
      SET IsVisible = 1
         ,VisibleDate = getUTCdate()
      WHERE TransactionId = @TransactionId
    SET @AffectedRows += @@rowcount

    SET @IsCompleted = 1
    SET @MinTransactionId = @TransactionId + 1
    WHILE @IsCompleted = 1
    BEGIN
      SET @TransactionId = @TransactionId + 1
      SET @IsCompleted = (SELECT IsCompleted FROM dbo.Transactions WHERE TransactionId = @TransactionId)
      IF @IsCompleted IS NULL -- transaction gap or no more transactions
      BEGIN
        SET @TransactionId = @TransactionId - 1 -- set back
        SELECT TOP 1 @TransactionId = TransactionId, @IsCompleted = IsCompleted FROM dbo.Transactions WHERE TransactionId > @TransactionId ORDER BY TransactionId -- find next
      END

      IF @IsCompleted = 1
        SET @MaxTransactionId = @TransactionId
    END

    IF @MaxTransactionId IS NOT NULL
    BEGIN
      UPDATE dbo.Transactions 
        SET IsVisible = 1
           ,VisibleDate = getUTCdate()
        WHERE TransactionId BETWEEN @MinTransactionId AND @MaxTransactionId
      SET @AffectedRows += @@rowcount
    END
  END

  SET @msg = 'ST='+convert(varchar,@TransactionId)
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@AffectedRows,@Text=@msg
END TRY
BEGIN CATCH
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
