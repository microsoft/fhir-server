-- This script should be empty (or all commented out) on final PR version.
-- If it is not empty, it can help to test intermediate states being introduced by diff script.
-- Example script below replaces *SearchParam tables by views

--DECLARE @Objects TABLE (Name varchar(100) PRIMARY KEY)
--INSERT INTO @Objects SELECT name FROM sys.objects WHERE type = 'u' AND name LIKE '%SearchParam'

--DECLARE @Tbl varchar(100)
--       ,@TblTable varchar(100)
--       ,@SQL varchar(max)

--WHILE EXISTS (SELECT * FROM @Objects)
--BEGIN
--  SET @Tbl = (SELECT TOP 1 Name FROM @Objects)
--  SET @TblTable = @Tbl+'_Table'
--  SET @SQL = ''

--  SELECT TOP 100 @SQL = @SQL + CASE WHEN @SQL <> '' THEN ',' ELSE '' END + CASE WHEN name = 'IsHistory' THEN 'IsHistory = convert(bit,0)' ELSE name END FROM sys.columns WHERE object_id = object_id(@Tbl) ORDER BY column_id
--  SET @SQL = 'CREATE VIEW '+@Tbl+' AS SELECT '+@SQL+' FROM '+@TblTable
  
--  BEGIN TRANSACTION

--  EXECUTE sp_rename @Tbl, @TblTable
--  EXECUTE(@SQL)

--  COMMIT TRANSACTION

--  DELETE FROM @Objects WHERE Name = @Tbl
--END
