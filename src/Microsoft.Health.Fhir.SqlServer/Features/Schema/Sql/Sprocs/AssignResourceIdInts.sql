CREATE PROCEDURE dbo.AssignResourceIdInts @Count int, @FirstIdInt bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'AssignResourceIdInts'
       ,@Mode varchar(200) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@FirstValueVar sql_variant
       ,@LastValueVar sql_variant
       ,@SequenceRangeFirstValue int

BEGIN TRY
  SET @FirstValueVar = NULL
  WHILE @FirstValueVar IS NULL
  BEGIN
    EXECUTE sys.sp_sequence_get_range @sequence_name = 'dbo.ResourceIdIntMapSequence', @range_size = @Count, @range_first_value = @FirstValueVar OUT, @range_last_value = @LastValueVar OUT
    SET @SequenceRangeFirstValue = convert(int,@FirstValueVar)
    IF @SequenceRangeFirstValue > convert(int,@LastValueVar)
      SET @FirstValueVar = NULL
  END

  SET @FirstIdInt = datediff_big(millisecond,'0001-01-01',sysUTCdatetime()) * 80000 + @SequenceRangeFirstValue
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  IF @@trancount > 0 ROLLBACK TRANSACTION
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error';
  THROW
END CATCH
GO
