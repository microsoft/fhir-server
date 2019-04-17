-- Enable RCSI

DECLARE @sql nvarchar(max) =  'ALTER DATABASE ' + DB_NAME() + ' SET READ_COMMITTED_SNAPSHOT ON'
EXEC(@sql);
