--DROP PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory
GO
CREATE PROCEDURE dbo.MergeResourcesDeleteInvisibleHistory @TransactionId bigint, @AffectedRows int = NULL OUT
AS
set nocount on
DECLARE @SP varchar(100) = object_name(@@procid)
       ,@Mode varchar(100) = 'T='+convert(varchar,@TransactionId)
       ,@st datetime = getUTCdate()
       ,@TypeId smallint

SET @AffectedRows = 0

BEGIN TRY  
  DELETE FROM dbo.Resource WHERE HistoryTransactionId = @TransactionId AND RawResource = 0xF
  SET @AffectedRows += @@rowcount
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Run',@Target='Resource',@Action='Delete',@Start=@st,@Rows=@AffectedRows

  -- TODO: Move to a separate stored procedure?
  UPDATE dbo.Resource SET TransactionId = NULL WHERE TransactionId = @TransactionId
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Target='Resource',@Action='Update',@Start=@st,@Rows=@@rowcount
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
