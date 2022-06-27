--DROP PROCEDURE dbo.PutShardTransaction
GO
CREATE PROCEDURE dbo.PutShardTransaction @TransactionId bigint
AS
set nocount on
DECLARE @SP varchar(100) = 'PutShardTransaction'
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)
       ,@st datetime = getUTCdate()

BEGIN TRY
  INSERT INTO dbo.ShardTransactions 
          ( TransactionId )
    SELECT @TransactionId
      WHERE NOT EXISTS (SELECT * FROM dbo.ShardTransactions WHERE TransactionId = @TransactionId)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@st;
  THROW
END CATCH
GO
