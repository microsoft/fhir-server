--DROP PROCEDURE dbo.MergeResourcesBeginTransaction
GO
CREATE PROCEDURE dbo.MergeResourcesBeginTransaction @Count int, @TransactionId bigint = 0 OUT, @MinResourceSurrogateId bigint = 0 OUT 
AS
set nocount on
DECLARE @SP varchar(100) = 'MergeResourcesBeginTransaction'
       ,@Mode varchar(100) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@LastValueVar sql_variant

BEGIN TRY
  -- Below logic is SQL implementation of current C# surrogate id helper extended for a batch
  -- I don't like it because it is not full proof, and can produce identical ids for different calls.
  EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = NULL, @range_last_value = @LastValueVar OUT
  SET @MinResourceSurrogateId = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + convert(int,@LastValueVar) - @Count
  
  -- This is a placeholder. It will change in future.
  SET @TransactionId = @MinResourceSurrogateId

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=NULL,@Text=@TransactionId
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @TransactionId bigint
--EXECUTE dbo.MergeResourcesBeginTransaction @Count = 500, @TransactionId = @TransactionId OUT
--SELECT @TransactionId
