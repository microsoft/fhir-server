--DROP PROCEDURE dbo.GetResourceSurrogateIdMaxSequence
GO
CREATE PROCEDURE dbo.GetResourceSurrogateIdMaxSequence @Count int, @MaxSequence bigint OUT
AS
set nocount on
DECLARE @SP varchar(100) = 'GetResourceSurrogateIdMaxSequence'
       ,@Mode varchar(100) = 'Cnt='+convert(varchar,@Count)
       ,@st datetime = getUTCdate()
       ,@Rows int

BEGIN TRY
  UPDATE dbo.ResourceSurrogateIdMaxSequence
    SET MaxSequence = MaxSequence + @Count
       ,@MaxSequence = MaxSequence + @Count
  SET @Rows = @@rowcount

  IF @Rows <> 1 RAISERROR('Unexpected number of rows in dbo.ResourceSurrogateIdMaxSequence table.', 18, 127)

  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@st,@Rows=@Rows,@Text=@MaxSequence
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error'
END CATCH
GO
