/*

*/
IF schema_id(N'administration') IS NULL
  EXECUTE (N'CREATE SCHEMA administration');

go

IF object_id(N'[administration].[set__defragment_index]', N'P') IS NOT NULL
  DROP PROCEDURE dbo.DefragmentIndex;

GO

SET ANSI_NULLS ON

GO

SET QUOTED_IDENTIFIER ON

GO

/*
	--
    -- RUN SCRIPT FOR DOCUMENTATION
    ------------------------------------------------
    DECLARE @schema   [sysname] = N'administration'
            , @object [sysname] = N'set__defragment_index';
    
    --
    SELECT N'['
           + object_schema_name([extended_properties].[major_id])
           + N'].['
           + CASE
               WHEN Object_name([objects].[parent_object_id]) IS NOT NULL
               THEN Object_name([objects].[parent_object_id])
                    + N'].[' + Object_name([objects].[object_id])
                    + N']'
               ELSE Object_name([objects].[object_id]) + N']'
                    + CASE
                        WHEN [parameters].[parameter_id] > 0
                        THEN COALESCE(N'.[' + [parameters].[name] + N']', N'')
                        ELSE N''
                      END
                    + CASE
                        WHEN columnproperty ([objects].[object_id], [parameters].[name], N'IsOutParam') = 1
                        THEN N' output'
                        ELSE N''
                      END
             END                           AS [object]
           , CASE
               WHEN [extended_properties].[minor_id] = 0
               THEN [objects].[type_desc]
               ELSE N'PARAMETER'
             END                           AS [type]
           , [extended_properties].[name]  AS [property]
           , [extended_properties].[value] AS [value]
    FROM   [sys].[extended_properties] AS [extended_properties]
           JOIN [sys].[objects] AS [objects]
             ON [objects].[object_id] = [extended_properties].[major_id]
           JOIN [sys].[schemas] AS [schemas]
             ON [schemas].[schema_id] = [objects].[schema_id]
           LEFT JOIN [sys].[parameters] AS [parameters]
                  ON [extended_properties].[major_id] = [parameters].[object_id]
                     AND [parameters].[parameter_id] = [extended_properties].[minor_id]
    WHERE  [schemas].[name] = @schema
           AND [objects].[name] = @object
    ORDER  BY [parameters].[parameter_id]
              , [object]
              , [type]
              , [property];  
*/
CREATE PROCEDURE  dbo.DefragmentIndex @maximum_fragmentation    [int] = 25
                                    , @minimum_page_count     [int] = 500
                                    , @fillfactor             [int] = NULL
                                    , @reorganize_demarcation [int] = 25
                                    , @defrag_count_limit     [int] = NULL
                                    , @output                 [xml] = NULL OUTPUT
                                    , @debug                  [bit] = 0
AS
  BEGIN
      DECLARE @schema                         [sysname]
              , @table                        [sysname]
              , @index                        [sysname]
              , @partition_number             [int]
              , @average_fragmentation_before [decimal](10, 2)
              , @average_fragmentation_after  [decimal](10, 2)
              , @sql                          [nvarchar](MAX)
              , @xml_builder                  [xml]
              , @start                        [datetimeoffset]
              , @complete                     [datetimeoffset]
              , @elapsed                      [decimal](10, 2)
              , @timestamp                    [datetimeoffset] = sysutcdatetime()
              , @this                         [nvarchar](1024) = QUOTENAME(DB_NAME()) + N'.'
                + QUOTENAME(OBJECT_SCHEMA_NAME(@@PROCID))
                + N'.' + QUOTENAME(OBJECT_NAME(@@PROCID));

      --
      -------------------------------------------
      SELECT @output = N'<index_list subject="' + @this
                       + '" timestamp="'
                       + CONVERT([sysname], @timestamp, 126) + '"/>'
             , @defrag_count_limit = COALESCE(@defrag_count_limit, 1);

      --
      -------------------------------------------
      SET @output.modify(N'insert attribute maximum_fragmentation {sql:variable("@maximum_fragmentation")} as last into (/*)[1]');
      SET @output.modify(N'insert attribute minimum_page_count {sql:variable("@minimum_page_count")} as last into (/*)[1]');

      IF @fillfactor IS NOT NULL
        SET @output.modify(N'insert attribute fillfactor {sql:variable("@fillfactor")} as last into (/*)[1]');

      SET @output.modify(N'insert attribute reorganize_demarcation {sql:variable("@reorganize_demarcation")} as last into (/*)[1]');
      SET @output.modify(N'insert attribute defrag_count_limit {sql:variable("@defrag_count_limit")} as last into (/*)[1]');

      --
      -------------------------------------------
      DECLARE [table_cursor] CURSOR FOR
        SELECT TOP(@defrag_count_limit) [schemas].[name]                                              AS [schema]
                                        , [tables].[name]                                             AS [table]
                                        , [indexes].[name]                                            AS [index]
                                        , [dm_db_index_physical_stats].[partition_number]             AS [partition_number]
                                        , [dm_db_index_physical_stats].[avg_fragmentation_in_percent] AS [average_fragmentation_before]
        FROM   [sys].[dm_db_index_physical_stats](DB_ID(), NULL, NULL, NULL, 'LIMITED') AS [dm_db_index_physical_stats]
               JOIN [sys].[indexes] AS [indexes]
                 ON [dm_db_index_physical_stats].[object_id] = [indexes].[object_id]
                    AND [dm_db_index_physical_stats].[index_id] = [indexes].[index_id]
               JOIN [sys].[tables] AS [tables]
                 ON [tables].[object_id] = [dm_db_index_physical_stats].[object_id]
               JOIN [sys].[schemas] AS [schemas]
                 ON [schemas].[schema_id] = [tables].[schema_id]
        WHERE  [indexes].[name] IS NOT NULL
               AND [dm_db_index_physical_stats].[avg_fragmentation_in_percent] > @maximum_fragmentation
               AND [dm_db_index_physical_stats].[page_count] > @minimum_page_count
        ORDER  BY [dm_db_index_physical_stats].[avg_fragmentation_in_percent] DESC
                  , [schemas].[name] ASC
                  , [tables].[name] ASC
                  , [dm_db_index_physical_stats].[partition_number] ASC;

      /*
      */
      IF @debug = 1
        BEGIN;
            SELECT TOP(@defrag_count_limit) [schemas].[name]                                              AS [schema]
                                            , [tables].[name]                                             AS [table]
                                            , [indexes].[name]                                            AS [index]
                                            , [dm_db_index_physical_stats].[partition_number]             AS [partition_number]
                                            , [dm_db_index_physical_stats].[avg_fragmentation_in_percent] AS [average_fragmentation_before]
            FROM   [sys].[dm_db_index_physical_stats](DB_ID(), NULL, NULL, NULL, 'LIMITED') AS [dm_db_index_physical_stats]
                   JOIN [sys].[indexes] AS [indexes]
                     ON [dm_db_index_physical_stats].[object_id] = [indexes].[object_id]
                        AND [dm_db_index_physical_stats].[index_id] = [indexes].[index_id]
                   JOIN [sys].[tables] AS [tables]
                     ON [tables].[object_id] = [dm_db_index_physical_stats].[object_id]
                   JOIN [sys].[schemas] AS [schemas]
                     ON [schemas].[schema_id] = [tables].[schema_id]
            WHERE  [indexes].[name] IS NOT NULL
                   AND [dm_db_index_physical_stats].[avg_fragmentation_in_percent] > @maximum_fragmentation
                   AND [dm_db_index_physical_stats].[page_count] > @minimum_page_count
            ORDER  BY [dm_db_index_physical_stats].[avg_fragmentation_in_percent] DESC
                      , [schemas].[name] ASC
                      , [tables].[name] ASC
                      , [dm_db_index_physical_stats].[partition_number] ASC;
        END;

      --
      ------------------------------------------- 
      BEGIN
          OPEN [table_cursor];

          FETCH NEXT FROM [table_cursor] INTO @schema
                                              , @table
                                              , @index
                                              , @partition_number
                                              , @average_fragmentation_before;

          WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @average_fragmentation_before > @reorganize_demarcation
                  BEGIN
                      SET @sql = N'alter index [' + @index + N'] on [' + @schema
                                 + N'].[' + @table + N'] rebuild ';

                      IF @fillfactor IS NOT NULL
                        BEGIN
                            SET @sql = @sql + N' with (fillfactor='
                                       + CAST(@fillfactor AS [sysname]) + N')';
                        END;

                      SET @sql = @sql + N' ; ';
                  END;
                ELSE
                  BEGIN
                      SET @sql = N'alter index [' + @index + N'] on [' + @schema
                                 + N'].[' + @table + N'] reorganize';
                  END;

                --
                -------------------------------
                IF @sql IS NOT NULL
                  BEGIN
                      --
                      ---------------------------
                      SET @start = sysutcdatetime();

                      -- 
                      -- defrag index
                      ---------------------------
                      IF @debug = 0
                        BEGIN;
                            EXECUTE sp_executesql
                              @sql = @sql;
                        END;
                      -- 
                      -- write operation to the console
                      ---------------------------
                      ELSE IF @debug = 1
                        BEGIN;
                            SELECT @this  AS [@this]
                                   , @sql AS [@sql];
                        END;

                      SET @complete = sysutcdatetime();
                      SET @elapsed = DATEDIFF(MILLISECOND, @start, @complete);
                      --
                      -- build output
                      ---------------------------
                      SET @xml_builder = (SELECT @schema                                                                               AS N'@schema'
                                                 , @table                                                                              AS N'@table'
                                                 , @index                                                                              AS N'@index'
                                                 , @average_fragmentation_before                                                       AS N'@average_fragmentation_before'
                                                 , CAST([dm_db_index_physical_stats].[avg_fragmentation_in_percent] AS decimal(10, 2)) AS N'@average_fragmentation_after'
                                                 , @elapsed                                                                            AS N'@elapsed_milliseconds'
                                                 , [dm_db_index_physical_stats].[partition_number]                                     AS N'@partition_number'
                                                 , @sql                                                                                AS N'sql'
                                          FROM   [sys].[dm_db_index_physical_stats](DB_ID(), NULL, NULL, NULL, 'LIMITED') AS [dm_db_index_physical_stats]
                                                 JOIN [sys].[indexes] AS [indexes]
                                                   ON [dm_db_index_physical_stats].[object_id] = [indexes].[object_id]
                                                      AND [dm_db_index_physical_stats].[index_id] = [indexes].[index_id]
                                                 JOIN [sys].[tables] AS [tables]
                                                   ON [tables].[object_id] = [dm_db_index_physical_stats].[object_id]
                                                 JOIN [sys].[schemas] AS [schemas]
                                                   ON [schemas].[schema_id] = [tables].[schema_id]
                                          WHERE  [schemas].[name] = @schema
                                                 AND [tables].[name] = @table
                                                 AND [indexes].[name] = @index
                                          FOR XML PATH(N'result'), ROOT(N'index'));

                      --
                      ---------------------------
                      IF @xml_builder IS NOT NULL
                        BEGIN
                            SET @output.modify(N'insert sql:variable("@xml_builder") as last into (/*)[1]');
                        END;
                  END;

                FETCH NEXT FROM [table_cursor] INTO @schema
                                                    , @table
                                                    , @index
                                                    , @partition_number
                                                    , @average_fragmentation_before;
            END;

          CLOSE [table_cursor];

          DEALLOCATE [table_cursor];
      END;
  END;

GO

--
------------------------------------------------- 
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'Rebuild all indexes over @maximum_fragmentation.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'execute_as'
  , @value = N'DECLARE @maximum_fragmentation    [int] = 5
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
	'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'execute_programmatically'
  , @value = N'DECLARE @count     [int] = 1
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
	'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@debug [bit] = 0 - If 1, do not defrag, write operation to console and continue.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@debug';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@minimum_page_count [INT] = 500 - Tables with page count less than this will not be defragmented. Default 500.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@minimum_page_count';

GO

--
------------------------------------------------- 
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@fillfactor [INT] - The fill factor to be used if an index is rebuilt. If NULL, the existing fill factor will be used for the index. DEFAULT - NULL. Referencing MS docs, FILLFACTOR should always be 0 (100%) unless read performance has been tested as non-100% values can case read performance degradation.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@fillfactor';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@reorganize_demarcation [INT] - The demarcation limit between a REORGANIZE vs REBUILD operation. Indexes having less than or equal to this level of fragmentation will be reorganized. Indexes with greater than this level of fragmentation will be rebuilt. DEFAULT - 25.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@reorganize_demarcation';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@maximum_fragmentation [INT] - The maximum fragmentation allowed before the procedure will attempt to defragment it. Indexes with fragmentation below this level will not be defragmented. DEFAULT 25.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@maximum_fragmentation';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@defrag_count_limit [INT] -  The maximum number of indexes to defragment. Used to limit the total time and resources to be consumed by a run. This will be used in conjunction with the @maximum_fragmentation parameter and should be considered to be the "TOP(n)" of indexes above the @maximum_fragmentation parameter. DEFAULT - NULL - Will be set to 1.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@defrag_count_limit';

GO

--
-------------------------------------------------  
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@output [XML] - An XML output construct containing the SQL used to defragment each index, the before and after fragmentation level, elapsed time in milliseconds, and other statistical information.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__defragment_index'
  , @level2type = N'parameter'
  , @level2name = N'@output';

GO 
