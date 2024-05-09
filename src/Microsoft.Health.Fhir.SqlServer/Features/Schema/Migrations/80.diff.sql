INSERT INTO dbo.Parameters (Id, Char) SELECT 'SearchParamsDeleteHistory', 'LogEvent'
EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='Start'

DECLARE @Tables TABLE (Name varchar(100))
DECLARE @Table varchar(100)
       ,@SQL nvarchar(max)
INSERT INTO @Tables SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam' AND name <> 'SearchParam' ORDER BY name

BEGIN TRY
  WHILE EXISTS (SELECT * FROM @Tables) -- Processing in ASC order
  BEGIN
    SET @Table = (SELECT TOP 1 Name FROM @Tables ORDER BY Name)
	PRINT 'Starting loop with table ' + @Table
    SET @SQL = 'DELETE FROM dbo.'+@Table+'
      WHERE ResourceSurrogateId in (
	    SELECT ResourceSurrogateId from Resource
	    WHERE ResourceSurrogateId in (
	      SELECT distinct ResourceSurrogateId FROM dbo.'+@Table+')
	  AND IsHistory = 1)'

	  EXECUTE sp_executeSQL @SQL
	  DELETE FROM @Tables WHERE Name = @Table
  END
END TRY
BEGIN CATCH
  IF error_number() = 1750 THROW -- Real error is before 1750, cannot trap in SQL.
  EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='Error';
  THROW
END CATCH

EXECUTE dbo.LogEvent @Process='SearchParamsDeleteHistory',@Status='End'
