--DROP PROCEDURE dbo.BeginTransaction
GO
CREATE PROCEDURE dbo.BeginTransaction
   @Definition          varchar(2000) = NULL
  ,@TransactionId       bigint        = NULL OUT 
  ,@HeartbeatDate       datetime      = NULL
  ,@TimeoutSeconds      int           = 600
  ,@ReturnRecordSet     bit           = 1
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'H='+isnull(convert(varchar,@HeartbeatDate,127),'NULL')
                           +' T='+isnull(convert(varchar,@TimeoutSeconds),'NULL')
       ,@st datetime = getUTCdate()

BEGIN TRY
  SET @TransactionId = NEXT VALUE FOR dbo.TransactionIdSequence

  INSERT INTO dbo.Transactions 
         (  Definition, TransactionId,                      HeartbeatDate)
    SELECT @Definition, @TransactionId, isnull(@HeartbeatDate,getUTCdate())

  IF @ReturnRecordSet = 1 EXECUTE dbo.GetTransactionById @TransactionId
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
-- Quick test code.
--EXECUTE BeginTransaction @IsPending=1,@ChangeTypeId=3, @BranchId=0,@UserId=0,@Metadata='<a></a>',@Description='Test',@BaseTransactionId=0
--SELECT * FROM Transaction
--EXECUTE StartCommitTransaction @TransactionId=-1
