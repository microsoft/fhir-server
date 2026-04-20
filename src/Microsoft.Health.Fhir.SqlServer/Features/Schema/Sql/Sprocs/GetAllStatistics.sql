--DROP PROCEDURE dbo.GetAllStatistics
GO
CREATE PROCEDURE dbo.GetAllStatistics
AS
set nocount on
DECLARE @i int = 0,
		@count int,
		@stat_name nvarchar(128),
		@table_name nvarchar(128),
		@min_rows int,
		@max_rows int,
		@column nvarchar(128);

CREATE TABLE #results
(
	stat_name nvarchar(128),
	table_name nvarchar(128),
	skew int,
	primary_column nvarchar(128),
);

CREATE TABLE #stats_density
(
	all_density float,
	average_length float,
	columns nvarchar(128),
);

CREATE TABLE #stats_histogram
(
	range_hi_key nvarchar(128),
	range_rows int,
	eq_rows int,
	distinct_range_rows int,
	avg_range_rows float,
);

SELECT DISTINCT
	s.name AS stat_name,
	OBJECT_NAME(s.OBJECT_ID) as table_name
INTO #stat_names
FROM sys.stats AS s
	JOIN sys.stats_columns AS sc
		ON s.object_id = sc.object_id AND s.stats_id = sc.stats_id
	JOIN sys.columns AS c
		ON sc.object_id = c.object_id AND sc.column_id = c.column_id
WHERE OBJECT_NAME(s.object_id) LIKE '%SearchParam'
AND s.name LIKE 'ST_%';

SELECT @count = count(*) FROM #stat_names;

WHILE @i < @count
BEGIN
	SELECT @stat_name = stat_name, @table_name = table_name
	FROM #stat_names
	ORDER BY table_name, stat_name
	OFFSET @i ROWS
	FETCH NEXT 1 ROWS ONLY;

	INSERT #stats_density
	EXEC('DBCC SHOW_STATISTICS(''' + @table_name + ''',' + @stat_name + ') WITH DENSITY_VECTOR');

	INSERT #stats_histogram
	EXEC('DBCC SHOW_STATISTICS(''' + @table_name + ''',' + @stat_name + ') WITH HISTOGRAM');

	SELECT TOP 1 @column = columns
	FROM #stats_density
	ORDER BY all_density ASC;

	SELECT @min_rows = MIN(eq_rows), @max_rows = MAX(eq_rows)
	FROM #stats_histogram;

	INSERT #results VALUES(@stat_name, @table_name, @max_rows/@min_rows, @column);

	TRUNCATE TABLE #stats_density;
	TRUNCATE TABLE #stats_histogram;

	SET @i = @i + 1;
END

SELECT * FROM #results

DROP TABLE #results
DROP TABLE #stat_names
DROP TABLE #stats_density
DROP TABLE #stats_histogram

GO
