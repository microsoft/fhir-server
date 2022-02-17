CREATE FUNCTION dbo.BuildStoreNumberAndErrorMessage (@ErrorNumber int, @ErrorMessage varchar(1000))
RETURNS varchar(1000)
BEGIN
  RETURN convert(varchar(1000), 'Error ' + convert(varchar(9), @ErrorNumber) + ': ' + @ErrorMessage)
END
GO
