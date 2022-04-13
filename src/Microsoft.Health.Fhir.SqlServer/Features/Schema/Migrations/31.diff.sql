
EXEC dbo.LogSchemaMigrationProgress 'Beginning schema migration to version 31.';
GO

IF schema_id(N'administration') IS NULL
    EXECUTE (N'CREATE SCHEMA administration');


GO
IF object_id(N'[administration].[set__defragment_index]', N'P') IS NOT NULL
    DROP PROCEDURE [administration].[set__defragment_index];


GO
SET ANSI_NULLS ON;


GO
SET QUOTED_IDENTIFIER ON;


GO
CREATE PROCEDURE dbo.DefragmentIndex
@maximum_fragmentation INT=25, @minimum_page_count INT=500, @fillfactor INT=NULL, @reorganize_demarcation INT=25, @defrag_count_limit INT=NULL, @output XML=NULL OUTPUT, @debug BIT=0
AS
BEGIN
    DECLARE @schema AS [sysname], @table AS [sysname], @index AS [sysname], @partition_number AS INT, @average_fragmentation_before AS DECIMAL (10, 2), @average_fragmentation_after AS DECIMAL (10, 2), @sql AS NVARCHAR (MAX), @xml_builder AS XML, @start AS DATETIMEOFFSET, @complete AS DATETIMEOFFSET, @elapsed AS DECIMAL (10, 2), @timestamp AS DATETIMEOFFSET = sysutcdatetime(), @this AS NVARCHAR (1024) = QUOTENAME(DB_NAME()) + N'.' + QUOTENAME(OBJECT_SCHEMA_NAME(@@PROCID)) + N'.' + QUOTENAME(OBJECT_NAME(@@PROCID));
    SELECT @output = N'<index_list subject="' + @this + '" timestamp="' + CONVERT ([sysname], @timestamp, 126) + '"/>',
           @defrag_count_limit = COALESCE (@defrag_count_limit, 1);
    SET @output.modify (N'insert attribute maximum_fragmentation {sql:variable("@maximum_fragmentation")} as last into (/*)[1]');
    SET @output.modify (N'insert attribute minimum_page_count {sql:variable("@minimum_page_count")} as last into (/*)[1]');
    IF @fillfactor IS NOT NULL
        SET @output.modify (N'insert attribute fillfactor {sql:variable("@fillfactor")} as last into (/*)[1]');
    SET @output.modify (N'insert attribute reorganize_demarcation {sql:variable("@reorganize_demarcation")} as last into (/*)[1]');
    SET @output.modify (N'insert attribute defrag_count_limit {sql:variable("@defrag_count_limit")} as last into (/*)[1]');
    DECLARE [table_cursor] CURSOR
        FOR SELECT   TOP (@defrag_count_limit) [schemas].[name] AS [schema],
                                               [tables].[name] AS [table],
                                               [indexes].[name] AS [index],
                                               [dm_db_index_physical_stats].[partition_number] AS [partition_number],
                                               [dm_db_index_physical_stats].[avg_fragmentation_in_percent] AS [average_fragmentation_before]
            FROM     [sys].[dm_db_index_physical_stats](DB_ID(), NULL, NULL, NULL, 'LIMITED') AS [dm_db_index_physical_stats]
                     INNER JOIN
                     [sys].[indexes] AS [indexes]
                     ON [dm_db_index_physical_stats].[object_id] = [indexes].[object_id]
                        AND [dm_db_index_physical_stats].[index_id] = [indexes].[index_id]
                     INNER JOIN
                     [sys].[tables] AS [tables]
                     ON [tables].[object_id] = [dm_db_index_physical_stats].[object_id]
                     INNER JOIN
                     [sys].[schemas] AS [schemas]
                     ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE    [indexes].[name] IS NOT NULL
                     AND [dm_db_index_physical_stats].[avg_fragmentation_in_percent] > @maximum_fragmentation
                     AND [dm_db_index_physical_stats].[page_count] > @minimum_page_count
            ORDER BY [dm_db_index_physical_stats].[avg_fragmentation_in_percent] DESC, [schemas].[name] ASC, [tables].[name] ASC, [dm_db_index_physical_stats].[partition_number] ASC;
    IF @debug = 1
        BEGIN
            SELECT   TOP (@defrag_count_limit) [schemas].[name] AS [schema],
                                               [tables].[name] AS [table],
                                               [indexes].[name] AS [index],
                                               [dm_db_index_physical_stats].[partition_number] AS [partition_number],
                                               [dm_db_index_physical_stats].[avg_fragmentation_in_percent] AS [average_fragmentation_before]
            FROM     [sys].[dm_db_index_physical_stats](DB_ID(), NULL, NULL, NULL, 'LIMITED') AS [dm_db_index_physical_stats]
                     INNER JOIN
                     [sys].[indexes] AS [indexes]
                     ON [dm_db_index_physical_stats].[object_id] = [indexes].[object_id]
                        AND [dm_db_index_physical_stats].[index_id] = [indexes].[index_id]
                     INNER JOIN
                     [sys].[tables] AS [tables]
                     ON [tables].[object_id] = [dm_db_index_physical_stats].[object_id]
                     INNER JOIN
                     [sys].[schemas] AS [schemas]
                     ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE    [indexes].[name] IS NOT NULL
                     AND [dm_db_index_physical_stats].[avg_fragmentation_in_percent] > @maximum_fragmentation
                     AND [dm_db_index_physical_stats].[page_count] > @minimum_page_count
            ORDER BY [dm_db_index_physical_stats].[avg_fragmentation_in_percent] DESC, [schemas].[name] ASC, [tables].[name] ASC, [dm_db_index_physical_stats].[partition_number] ASC;
        END
    BEGIN
        OPEN [table_cursor];
        FETCH NEXT FROM [table_cursor] INTO @schema, @table, @index, @partition_number, @average_fragmentation_before;
        WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @average_fragmentation_before > @reorganize_demarcation
                    BEGIN
                        SET @sql = N'alter index [' + @index + N'] on [' + @schema + N'].[' + @table + N'] rebuild ';
                        IF @fillfactor IS NOT NULL
                            BEGIN
                                SET @sql = @sql + N' with (fillfactor=' + CAST (@fillfactor AS [sysname]) + N')';
                            END
                        SET @sql = @sql + N' ; ';
                    END
                ELSE
                    BEGIN
                        SET @sql = N'alter index [' + @index + N'] on [' + @schema + N'].[' + @table + N'] reorganize';
                    END
                IF @sql IS NOT NULL
                    BEGIN
                        SET @start = sysutcdatetime();
                        IF @debug = 0
                            BEGIN
                                EXECUTE sp_executesql @sql = @sql;
                            END
                        ELSE
                            IF @debug = 1
                                BEGIN
                                    SELECT @this AS [@this],
                                           @sql AS [@sql];
                                END
                        SET @complete = sysutcdatetime();
                        SET @elapsed = DATEDIFF(MILLISECOND, @start, @complete);
                        SET @xml_builder = (SELECT @schema AS N'@schema',
                                                   @table AS N'@table',
                                                   @index AS N'@index',
                                                   @average_fragmentation_before AS N'@average_fragmentation_before',
                                                   CAST ([dm_db_index_physical_stats].[avg_fragmentation_in_percent] AS DECIMAL (10, 2)) AS N'@average_fragmentation_after',
                                                   @elapsed AS N'@elapsed_milliseconds',
                                                   [dm_db_index_physical_stats].[partition_number] AS N'@partition_number',
                                                   @sql AS N'sql'
                                            FROM   [sys].[dm_db_index_physical_stats](DB_ID(), NULL, NULL, NULL, 'LIMITED') AS [dm_db_index_physical_stats]
                                                   INNER JOIN
                                                   [sys].[indexes] AS [indexes]
                                                   ON [dm_db_index_physical_stats].[object_id] = [indexes].[object_id]
                                                      AND [dm_db_index_physical_stats].[index_id] = [indexes].[index_id]
                                                   INNER JOIN
                                                   [sys].[tables] AS [tables]
                                                   ON [tables].[object_id] = [dm_db_index_physical_stats].[object_id]
                                                   INNER JOIN
                                                   [sys].[schemas] AS [schemas]
                                                   ON [schemas].[schema_id] = [tables].[schema_id]
                                            WHERE  [schemas].[name] = @schema
                                                   AND [tables].[name] = @table
                                                   AND [indexes].[name] = @index
                                            FOR    XML PATH (N'result'), ROOT (N'index'));
                        IF @xml_builder IS NOT NULL
                            BEGIN
                                SET @output.modify (N'insert sql:variable("@xml_builder") as last into (/*)[1]');
                            END
                    END
                FETCH NEXT FROM [table_cursor] INTO @schema, @table, @index, @partition_number, @average_fragmentation_before;
            END
        CLOSE [table_cursor];
        DEALLOCATE [table_cursor];
    END
END


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'Rebuild all indexes over @maximum_fragmentation.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index';


GO
EXECUTE sys.sp_addextendedproperty @name = N'execute_as', @value = N'DECLARE @maximum_fragmentation    [int] = 5
        , @minimum_page_count     [int] = 500
        , @fillfactor             [int] = NULL
        , @reorganize_demarcation [int] = 25
        , @defrag_count_limit     [int] = 25
        , @output                 [xml]
        , @debug                  [bit] = 1;

EXECUTE [administration].[set__defragment_index]
  @maximum_fragmentation=@maximum_fragmentation
  , @minimum_page_count=@minimum_page_count
  , @fillfactor=@fillfactor
  , @reorganize_demarcation=@reorganize_demarcation
  , @defrag_count_limit=@defrag_count_limit
  , @output=@output OUTPUT
  , @debug = @debug;

SELECT @output AS [output];
	', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index';


GO
EXECUTE sys.sp_addextendedproperty @name = N'execute_programmatically', @value = N'DECLARE @count     [int] = 1
        , @message [sysname]
        , @loop    [int] = 3;

		WHILE @count > 0
		  /*AND @loop > 0*/
		  BEGIN;
			  DECLARE @maximum_fragmentation    [int] = 5
					  , @minimum_page_count     [int] = 500
					  , @fillfactor             [int] = NULL
					  , @reorganize_demarcation [int] = 25
					  , @defrag_count_limit     [int] = 2
					  , @output                 [xml] = NULL
					  , @debug                  [bit] = 0;

			  EXECUTE [administration].[set__defragment_index]
				@maximum_fragmentation=@maximum_fragmentation
				, @minimum_page_count=@minimum_page_count
				, @fillfactor=@fillfactor
				, @reorganize_demarcation=@reorganize_demarcation
				, @defrag_count_limit=@defrag_count_limit
				, @output=@output OUTPUT
				, @debug = @debug;

			  SELECT @output AS [output];

			  SELECT @count = @output.value(''count (/index_list/index/result)'', ''[int]'');

			  SELECT @message = N''@count('' + cast(@count AS [sysname]) + N'')'';

			  RAISERROR (@message,0,1) WITH NOWAIT;

			  SET @loop = @loop - 1;
		  END; 
	', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@debug [bit] = 0 - If 1, do not defrag, write operation to console and continue.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@debug';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@minimum_page_count [INT] = 500 - Tables with page count less than this will not be defragmented. Default 500.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@minimum_page_count';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@fillfactor [INT] - The fill factor to be used if an index is rebuilt. If NULL, the existing fill factor will be used for the index. DEFAULT - NULL. Referencing MS docs, FILLFACTOR should always be 0 (100%) unless read performance has been tested as non-100% values can case read performance degradation.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@fillfactor';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@reorganize_demarcation [INT] - The demarcation limit between a REORGANIZE vs REBUILD operation. Indexes having less than or equal to this level of fragmentation will be reorganized. Indexes with greater than this level of fragmentation will be rebuilt. DEFAULT - 25.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@reorganize_demarcation';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@maximum_fragmentation [INT] - The maximum fragmentation allowed before the procedure will attempt to defragment it. Indexes with fragmentation below this level will not be defragmented. DEFAULT 25.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@maximum_fragmentation';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@defrag_count_limit [INT] -  The maximum number of indexes to defragment. Used to limit the total time and resources to be consumed by a run. This will be used in conjunction with the @maximum_fragmentation parameter and should be considered to be the "TOP(n)" of indexes above the @maximum_fragmentation parameter. DEFAULT - NULL - Will be set to 1.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@defrag_count_limit';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@output [XML] - An XML output construct containing the SQL used to defragment each index, the before and after fragmentation level, elapsed time in milliseconds, and other statistical information.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__defragment_index', @level2type = N'parameter', @level2name = N'@output';

GO

IF schema_id(N'administration') IS NULL
    EXECUTE (N'CREATE SCHEMA administration');


GO
IF object_id(N'[administration].[set__update_statistics]', N'P') IS NOT NULL
    DROP PROCEDURE [administration].[set__update_statistics];


GO
SET ANSI_NULLS ON;


GO
SET QUOTED_IDENTIFIER ON;


GO
CREATE PROCEDURE dbo.UpdateStatistics
@table_filter [sysname]=NULL
AS
BEGIN
    DECLARE @view AS NVARCHAR (1024), @sql AS NVARCHAR (MAX);
    IF @table_filter IS NULL
        BEGIN
            PRINT N'Executing [sys].[sp_updatestats] to update statistics on all objects other than indexed views.';
            EXECUTE [sys].[sp_updatestats] ;
            PRINT N'Executing [sys].[sp_updatestats] complete.';
            PRINT N'Updating statistics for indexed views.';
            DECLARE [view_cursor] CURSOR LOCAL FAST_FORWARD
                FOR SELECT   QUOTENAME([schemas].[name], N'[') + N'.' + QUOTENAME([objects].[name], N'[') AS [view_name]
                    FROM     [sys].[objects] AS [objects]
                             INNER JOIN
                             [sys].[schemas] AS [schemas]
                             ON [schemas].[schema_id] = [objects].[schema_id]
                             INNER JOIN
                             [sys].[indexes] AS [indexes]
                             ON [indexes].[object_id] = [objects].[object_id]
                             INNER JOIN
                             [sys].[sysindexes] AS [sysindexes]
                             ON [sysindexes].id = [indexes].[object_id]
                                AND [sysindexes].[indid] = [indexes].[index_id]
                    WHERE    [objects].[type] = 'V'
                    GROUP BY QUOTENAME([schemas].[name], N'[') + N'.' + QUOTENAME([objects].[name], N'[')
                    HAVING   MAX([sysindexes].[rowmodctr]) > 0;
        END
    BEGIN
        OPEN [view_cursor];
        FETCH NEXT FROM [view_cursor] INTO @view;
        WHILE (@@FETCH_STATUS = 0)
            BEGIN
                PRINT N'   Updating stats for view ' + @view;
                SET @sql = N'update statistics ' + @view;
                EXECUTE (@sql);
                FETCH NEXT FROM [view_cursor] INTO @view;
            END
        CLOSE [view_cursor];
        DEALLOCATE [view_cursor];
    END
    PRINT N'Updating statistics for indexed views. complete.';
END


GO
EXECUTE [sys].sp_addextendedproperty @name = N'description', @value = N'Procedure to update all statistics including indexed views.
  Based on a script from:
  Rhys Jones, 7th Feb 2008
	http://www.rmjcs.com/SQLServer/ThingsYouMightNotKnow/sp_updatestatsDoesNotUpdateIndexedViewStats/tabid/414/Default.aspx
	Update stats in indexed views because indexed view stats are not updated by sp_updatestats.
	Only does an update if rowmodctr is non-zero.
	No error handling, does not deal with disabled clustered indexes.
	Does not respect existing sample rate.
	[sys].sysindexes.rowmodctr is not completely reliable in SQL Server 2005.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__update_statistics';


GO
EXECUTE [sys].sp_addextendedproperty @name = N'TODO', @value = N'Refactor to use @table_filter properly.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__update_statistics';


GO
EXECUTE [sys].sp_addextendedproperty @name = N'package_administration', @value = N'label_only', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__update_statistics';


GO
EXECUTE [sys].sp_addextendedproperty @name = N'execute_as', @value = N'execute [administration].[set__update_statistics];', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__update_statistics';


GO
EXECUTE sys.sp_addextendedproperty @name = N'description', @value = N'@table [sysname] NOT NULL - optional parameter, if used, constrains UPDATE STATISTICS to tables matching on LIKE syntax.', @level0type = N'schema', @level0name = N'administration', @level1type = N'procedure', @level1name = N'set__update_statistics', @level2type = N'parameter', @level2name = N'@table_filter';

GO

GO
