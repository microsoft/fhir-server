CREATE OR ALTER FUNCTION dbo.GetSurrogateIdBaseFromDate(@LastUpdated datetime2)
RETURNS bigint
AS
BEGIN
  RETURN datediff_big(millisecond,'0001-01-01',@LastUpdated) * 80000
END
GO
CREATE OR ALTER FUNCTION dbo.GetDateFromSurrogateId(@SurrogateId bigint)
RETURNS datetime2
AS
BEGIN
  RETURN dateadd(millisecond
                ,convert(int, @SurrogateId / 80000 % (1000 * 60))
                        ,dateadd(minute, convert(int, @SurrogateId / 80000 / 1000 / 60)
                        ,convert(datetime2,'0001-01-01')
                        )
                )
END
GO
