CREATE FUNCTION dbo.GetSurrogateIdBaseFromDate(@LastUpdated datetime2)
RETURNS bigint
AS
BEGIN
  RETURN datediff_big(millisecond,'0001-01-01',@LastUpdated) * 80000
END
GO
