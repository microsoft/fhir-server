--DROP PROCEDURE dbo.GetPreciseStatisticsProperties
GO
CREATE PROCEDURE dbo.GetPreciseStatisticsProperties
AS
set nocount on
DECLARE  @i int = 0
	    ,@count int
	    ,@stat_name nvarchar(128)
	    ,@table_name nvarchar(128)
        ,@min_rows int
	    ,@max_rows int;

DECLARE @results TABLE
(
	 stat_name nvarchar(128)
	,table_name nvarchar(128)
	,skew int
);

DECLARE @stat_names TABLE
(
	 stat_name nvarchar(128)
	,table_name nvarchar(128)
);

DECLARE @stats_histogram TABLE
(
	 range_hi_key nvarchar(128)
	,range_rows int
	,eq_rows int
	,distinct_range_rows int
	,avg_range_rows float
);

INSERT @stat_names
SELECT DISTINCT
	s.name AS stat_name,
	OBJECT_NAME(s.OBJECT_ID) as table_name
FROM sys.stats AS s
	JOIN sys.stats_columns AS sc
		ON s.object_id = sc.object_id AND s.stats_id = sc.stats_id
	JOIN sys.columns AS c
		ON sc.object_id = c.object_id AND sc.column_id = c.column_id
WHERE s.name LIKE 'ST_%';

SELECT @count = count(*) FROM @stat_names;

WHILE @i < @count
BEGIN
	SELECT @stat_name = stat_name, @table_name = table_name
	FROM @stat_names
	ORDER BY table_name, stat_name
	OFFSET @i ROWS
	FETCH NEXT 1 ROWS ONLY;

	PRINT @stat_name + ' ' + @table_name;

	INSERT @stats_histogram
	EXEC('DBCC SHOW_STATISTICS(''' + @table_name + ''',' + @stat_name + ') WITH HISTOGRAM');

	SELECT @min_rows = MIN(eq_rows), @max_rows = MAX(eq_rows)
	FROM @stats_histogram;

	INSERT @results VALUES(@stat_name, @table_name, @max_rows/@min_rows);

	DELETE FROM @stats_histogram

	SET @i = @i + 1;
END

SELECT * FROM @results

GO
