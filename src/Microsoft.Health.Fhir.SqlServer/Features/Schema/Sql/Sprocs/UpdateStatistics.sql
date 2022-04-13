IF schema_id(N'administration') IS NULL
  EXECUTE (N'CREATE SCHEMA administration');

go

IF object_id(N'[administration].[set__update_statistics]', N'P') IS NOT NULL
  DROP PROCEDURE dbo.UpdateStatistics;

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
            , @object [sysname] = N'set__update_statistics';
    
    --
    -------------------------------------------------
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
CREATE PROCEDURE dbo.UpdateStatistics @table_filter [sysname] = NULL
AS
  BEGIN
      DECLARE @view  nvarchar(1024)
              , @sql nvarchar(MAX);

      --
      -----------------------------------------------
      IF @table_filter IS NULL
        BEGIN
            PRINT N'Executing [sys].[sp_updatestats] to update statistics on all objects other than indexed views.';

            --
            -- update statistics on all objects other than indexed views
            -------------------------------------
            EXECUTE [sys].[sp_updatestats];

            PRINT N'Executing [sys].[sp_updatestats] complete.';

            --
            -- update statistics for indexed views
            -------------------------------------
            PRINT N'Updating statistics for indexed views.';

            DECLARE [view_cursor] CURSOR LOCAL FAST_FORWARD FOR
              SELECT QUOTENAME([schemas].[name], N'[') + N'.'
                     + QUOTENAME([objects].[name], N'[') AS [view_name]
              FROM   [sys].[objects] [objects]
                     INNER JOIN [sys].[schemas] [schemas]
                             ON [schemas].[schema_id] = [objects].[schema_id]
                     INNER JOIN [sys].[indexes] [indexes]
                             ON [indexes].[object_id] = [objects].[object_id]
                     INNER JOIN [sys].[sysindexes] [sysindexes]
                             ON [sysindexes].id = [indexes].[object_id]
                                AND [sysindexes].[indid] = [indexes].[index_id]
              WHERE  [objects].[type] = 'V'
              GROUP  BY QUOTENAME([schemas].[name], N'[') + N'.'
                        + QUOTENAME([objects].[name], N'[')
              HAVING MAX([sysindexes].[rowmodctr]) > 0;
        END;

      --
      -----------------------------------------
      BEGIN
          OPEN [view_cursor];

          FETCH NEXT FROM [view_cursor] INTO @view;

          WHILE ( @@FETCH_STATUS = 0 )
            BEGIN
                PRINT N'   Updating stats for view ' + @view;

                SET @sql = N'update statistics ' + @view;

                EXECUTE (@sql);

                FETCH NEXT FROM [view_cursor] INTO @view;
            END;

          CLOSE [view_cursor];

          DEALLOCATE [view_cursor];
      END;

      PRINT N'Updating statistics for indexed views. complete.';
  END;

GO

--
------------------------------------------------- 
EXEC [sys].sp_addextendedproperty
  @name = N'description'
  , @value = N'Procedure to update all statistics including indexed views.
  Based on a script from:
  Rhys Jones, 7th Feb 2008
	http://www.rmjcs.com/SQLServer/ThingsYouMightNotKnow/sp_updatestatsDoesNotUpdateIndexedViewStats/tabid/414/Default.aspx
	Update stats in indexed views because indexed view stats are not updated by sp_updatestats.
	Only does an update if rowmodctr is non-zero.
	No error handling, does not deal with disabled clustered indexes.
	Does not respect existing sample rate.
	[sys].sysindexes.rowmodctr is not completely reliable in SQL Server 2005.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__update_statistics';

GO

--
------------------------------------------------- 
EXEC [sys].sp_addextendedproperty
  @name = N'TODO'
  , @value = N'Refactor to use @table_filter properly.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__update_statistics';

GO

--
------------------------------------------------- 
EXEC [sys].sp_addextendedproperty
  @name = N'package_administration'
  , @value = N'label_only'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__update_statistics';

GO

--
------------------------------------------------- 
EXEC [sys].sp_addextendedproperty
  @name = N'execute_as'
  , @value = N'execute [administration].[set__update_statistics];'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__update_statistics';

GO

--
------------------------------------------------- 
EXEC sys.sp_addextendedproperty
  @name = N'description'
  , @value = N'@table [sysname] NOT NULL - optional parameter, if used, constrains UPDATE STATISTICS to tables matching on LIKE syntax.'
  , @level0type = N'schema'
  , @level0name = N'administration'
  , @level1type = N'procedure'
  , @level1name = N'set__update_statistics'
  , @level2type = N'parameter'
  , @level2name = N'@table_filter';

GO 
