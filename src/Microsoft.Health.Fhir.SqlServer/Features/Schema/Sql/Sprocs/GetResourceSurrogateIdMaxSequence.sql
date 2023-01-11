--DROP PROCEDURE dbo.GetResourceSurrogateIdMaxSequence
GO
CREATE PROCEDURE dbo.GetResourceSurrogateIdMaxSequence @Count int, @MaxSequence bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdMaxSequence'
       ,@Mode varchar(100) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime2 = sysUTCdatetime()
       ,@LastValueVar sql_variant

BEGIN TRY
  -- Below logic is SQL implementation of current C# surrogate id helper extended for a batch
  -- I don't like it because it is not full proof, and can produce identical ids for different calls.
  EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceSurrogateIdUniquifierSequence', @range_size = @Count, @range_first_value = NULL, @range_last_value = @LastValueVar OUT
  SET @MaxSequence = datediff_big(millisecond,'0001-01-01',@st) * 80000 + convert(int,@LastValueVar)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=NULL,@Text=@MaxSequence
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
--DECLARE @MaxSequence bigint
--EXECUTE dbo.GetResourceSurrogateIdMaxSequence @Count = 500, @MaxSequence = @MaxSequence OUT
--SELECT @MaxSequence
