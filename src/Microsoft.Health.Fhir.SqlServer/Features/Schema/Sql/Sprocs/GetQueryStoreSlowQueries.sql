--DROP PROCEDURE dbo.GetQueryStoreSlowQueries
GO
CREATE PROCEDURE dbo.GetQueryStoreSlowQueries
    @StartTime datetimeoffset(7) = NULL
   ,@EndTime datetimeoffset(7) = NULL
   ,@Top int = 20
   ,@Offset int = 0
   ,@OrderBy varchar(32) = 'TotalDuration'
   ,@MinExecutions bigint = 1
   ,@QueryTextContains nvarchar(256) = NULL
WITH EXECUTE AS 'dbo'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ProcedureName varchar(100) = OBJECT_NAME(@@PROCID);
    DECLARE @AuditMode varchar(200) = 'QueryStoreSlowQueries';
    DECLARE @AuditStartTime datetime = GETUTCDATE();
    DECLARE @AuditText nvarchar(3500);
    DECLARE @RowsReturned bigint;
    DECLARE @ResolvedStartTime datetimeoffset(7);
    DECLARE @ResolvedEndTime datetimeoffset(7);
    DECLARE @OrderByNormalized varchar(32);
    DECLARE @QueryTextPattern nvarchar(514);
    DECLARE @QueryTextFilterLength int;
    DECLARE @QueryStoreState nvarchar(60);
    DECLARE @QueryStoreReadOnlyReason bigint;
    DECLARE @WaitStatsCaptureMode nvarchar(60);
    DECLARE @WaitStatsStatus varchar(32);
    DECLARE @SlowQueriesProcedureObjectId int = OBJECT_ID(N'dbo.GetQueryStoreSlowQueries');
    DECLARE @PlanDiagnosticsProcedureObjectId int = OBJECT_ID(N'dbo.GetQueryStorePlanDiagnostics');
    DECLARE @StatisticsHealthProcedureObjectId int = OBJECT_ID(N'dbo.GetStatisticsHealth');

    IF @ProcedureName IS NULL
        SET @ProcedureName = 'GetQueryStoreSlowQueries';

    SET @AuditText = CONCAT(
        N'OriginalLogin=', ORIGINAL_LOGIN(),
        N';EffectivePrincipal=', USER_NAME());

    BEGIN TRY
    SET @Top = ISNULL(@Top, 20);
    SET @Offset = ISNULL(@Offset, 0);
    SET @MinExecutions = ISNULL(@MinExecutions, 1);
    SET @OrderBy = ISNULL(@OrderBy, 'TotalDuration');
    SET @ResolvedEndTime = SWITCHOFFSET(ISNULL(@EndTime, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')), '+00:00');
    SET @ResolvedStartTime = SWITCHOFFSET(ISNULL(@StartTime, DATEADD(hour, -1, @ResolvedEndTime)), '+00:00');
    SET @QueryTextContains = NULLIF(LTRIM(RTRIM(@QueryTextContains)), N'');
    SET @QueryTextFilterLength = ISNULL(LEN(@QueryTextContains), 0);

    SELECT
        @QueryStoreState = actual_state_desc,
        @QueryStoreReadOnlyReason = readonly_reason,
        @WaitStatsCaptureMode = wait_stats_capture_mode_desc
    FROM sys.database_query_store_options;

    SET @WaitStatsStatus =
        CASE @WaitStatsCaptureMode
            WHEN N'ON' THEN 'Available'
            WHEN N'OFF' THEN 'Disabled'
            ELSE 'Unavailable'
        END;
    SET @AuditText = CONCAT(
        @AuditText,
        N';StartTimeUtc=', CONVERT(nvarchar(33), @ResolvedStartTime, 127),
        N';EndTimeUtc=', CONVERT(nvarchar(33), @ResolvedEndTime, 127),
        N';OrderBy=', @OrderBy,
        N';Top=', @Top,
        N';Offset=', @Offset,
        N';MinExecutions=', @MinExecutions,
        N';QueryTextFilterPresent=', CASE WHEN @QueryTextContains IS NULL THEN N'0' ELSE N'1' END,
        N';QueryTextFilterLength=', @QueryTextFilterLength,
        N';QueryStoreState=', ISNULL(@QueryStoreState, N'Unknown'),
        N';QueryStoreReadOnlyReason=', ISNULL(CONVERT(nvarchar(20), @QueryStoreReadOnlyReason), N'Unknown'),
        N';WaitStatsCaptureMode=', ISNULL(@WaitStatsCaptureMode, N'Unknown'));

        EXECUTE dbo.LogEvent
            @Process = @ProcedureName,
            @Mode = @AuditMode,
            @Status = 'Start',
            @Text = @AuditText;

        IF @Top < 1 OR @Top > 100
            THROW 50400, '@Top must be between 1 and 100.', 1;

        IF @Offset < 0 OR @Offset > 10000
            THROW 50401, '@Offset must be between 0 and 10000.', 1;

        IF @MinExecutions < 1
            THROW 50402, '@MinExecutions must be positive.', 1;

        IF @ResolvedStartTime >= @ResolvedEndTime
            THROW 50403, '@StartTime must precede @EndTime.', 1;

        IF @ResolvedEndTime > DATEADD(hour, 24, @ResolvedStartTime)
            THROW 50404, 'The requested time range must not exceed 24 hours.', 1;

        IF @QueryTextContains IS NOT NULL
           AND LEN(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(@QueryTextContains, N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N''), NCHAR(160), N'')) = 0
            THROW 50405, '@QueryTextContains must not be whitespace only.', 1;

        IF @QueryTextContains IS NOT NULL
           AND (@QueryTextFilterLength < 3 OR @QueryTextFilterLength > 256)
            THROW 50406, '@QueryTextContains must contain between 3 and 256 characters after trimming.', 1;

        SET @OrderByNormalized =
            CASE
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'TotalDuration' THEN 'TotalDuration'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'AverageDuration' THEN 'AverageDuration'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'MaximumDuration' THEN 'MaximumDuration'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'TotalCpu' THEN 'TotalCpu'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'AverageCpu' THEN 'AverageCpu'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'LogicalReads' THEN 'LogicalReads'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'Executions' THEN 'Executions'
                WHEN @OrderBy COLLATE Latin1_General_100_CI_AS = 'TotalWait' THEN 'TotalWait'
            END;

        IF @OrderByNormalized IS NULL
            THROW 50407, '@OrderBy is not supported.', 1;

        IF ISNULL(@QueryStoreState, N'') NOT IN (N'READ_WRITE', N'READ_ONLY')
            THROW 50408, 'Query Store is not enabled and readable.', 1;

        SET @QueryTextPattern =
            CASE
                WHEN @QueryTextContains IS NULL THEN NULL
                ELSE N'%' + REPLACE(REPLACE(REPLACE(REPLACE(@QueryTextContains, N'~', N'~~'), N'%', N'~%'), N'_', N'~_'), N'[', N'~[') + N'%'
            END;

        ;WITH RuntimeStatsRows AS
        (
            SELECT
                rs.plan_id AS PlanId,
                rs.execution_type AS ExecutionType,
                rs.runtime_stats_interval_id AS RuntimeStatsIntervalId,
                rs.runtime_stats_id AS RuntimeStatsId,
                rs.count_executions AS RegularExecutionCount,
                CONVERT(decimal(38, 4), rs.avg_duration) AS AverageDurationMicroseconds,
                CONVERT(decimal(38, 0), rs.min_duration) AS MinimumDurationMicroseconds,
                CONVERT(decimal(38, 0), rs.max_duration) AS MaximumDurationMicroseconds,
                CONVERT(decimal(38, 0), rs.last_duration) AS LastDurationMicroseconds,
                CONVERT(decimal(38, 4), rs.avg_cpu_time) AS AverageCpuMicroseconds,
                CONVERT(decimal(38, 4), rs.avg_logical_io_reads) AS AverageLogicalReads,
                CONVERT(decimal(38, 4), rs.avg_physical_io_reads) AS AveragePhysicalReads,
                CONVERT(decimal(38, 4), rs.avg_logical_io_writes) AS AverageLogicalWrites,
                CONVERT(decimal(38, 4), rs.avg_rowcount) AS AverageRowCount,
                CONVERT(decimal(38, 0), rs.max_rowcount) AS MaximumRowCount,
                SWITCHOFFSET(rs.first_execution_time, '+00:00') AS FirstExecutionTimeUtc,
                SWITCHOFFSET(rs.last_execution_time, '+00:00') AS LastExecutionTimeUtc
            FROM sys.query_store_runtime_stats AS rs
            INNER JOIN sys.query_store_runtime_stats_interval AS rsi
                ON rsi.runtime_stats_interval_id = rs.runtime_stats_interval_id
            -- The baseline is regular executions only. An explicit execution-type input belongs here if added later.
            WHERE rs.execution_type = 0
              AND rsi.start_time < @ResolvedEndTime
              AND rsi.end_time > @ResolvedStartTime
        ),
        RankedRuntimeStatsRows AS
        (
            SELECT
                rs.*,
                ROW_NUMBER() OVER
                (
                    PARTITION BY rs.PlanId, rs.ExecutionType, rs.RuntimeStatsIntervalId
                    ORDER BY rs.LastExecutionTimeUtc DESC, rs.RuntimeStatsIntervalId DESC, rs.RuntimeStatsId DESC
                ) AS LastValueRank
            FROM RuntimeStatsRows AS rs
        ),
        CollapsedRuntimeStats AS
        (
            SELECT
                rs.PlanId,
                rs.ExecutionType,
                rs.RuntimeStatsIntervalId,
                SUM(rs.RegularExecutionCount) AS RegularExecutionCount,
                CONVERT(decimal(38, 4), SUM(CONVERT(decimal(38, 4), rs.AverageDurationMicroseconds * CONVERT(decimal(19, 0), rs.RegularExecutionCount)))) AS TotalDurationMicroseconds,
                CONVERT(decimal(38, 4), SUM(CONVERT(decimal(38, 4), rs.AverageCpuMicroseconds * CONVERT(decimal(19, 0), rs.RegularExecutionCount)))) AS TotalCpuMicroseconds,
                CONVERT(decimal(38, 4), SUM(CONVERT(decimal(38, 4), rs.AverageLogicalReads * CONVERT(decimal(19, 0), rs.RegularExecutionCount)))) AS TotalLogicalReads,
                CONVERT(decimal(38, 4), SUM(CONVERT(decimal(38, 4), rs.AveragePhysicalReads * CONVERT(decimal(19, 0), rs.RegularExecutionCount)))) AS TotalPhysicalReads,
                CONVERT(decimal(38, 4), SUM(CONVERT(decimal(38, 4), rs.AverageLogicalWrites * CONVERT(decimal(19, 0), rs.RegularExecutionCount)))) AS TotalLogicalWrites,
                CONVERT(decimal(38, 4), SUM(CONVERT(decimal(38, 4), rs.AverageRowCount * CONVERT(decimal(19, 0), rs.RegularExecutionCount)))) AS TotalRowCount,
                MIN(rs.MinimumDurationMicroseconds) AS MinimumDurationMicroseconds,
                MAX(rs.MaximumDurationMicroseconds) AS MaximumDurationMicroseconds,
                MAX(rs.MaximumRowCount) AS MaximumRowCount,
                MIN(rs.FirstExecutionTimeUtc) AS FirstExecutionTimeUtc,
                MAX(CASE WHEN rs.LastValueRank = 1 THEN rs.LastExecutionTimeUtc END) AS LastExecutionTimeUtc,
                MAX(CASE WHEN rs.LastValueRank = 1 THEN rs.LastDurationMicroseconds END) AS LastDurationMicroseconds,
                MAX(CASE WHEN rs.LastValueRank = 1 THEN rs.RuntimeStatsId END) AS LastRuntimeStatsId
            FROM RankedRuntimeStatsRows AS rs
            GROUP BY
                rs.PlanId,
                rs.ExecutionType,
                rs.RuntimeStatsIntervalId
        ),
        RankedCollapsedRuntimeStats AS
        (
            SELECT
                rs.*,
                ROW_NUMBER() OVER
                (
                    PARTITION BY rs.PlanId
                    ORDER BY rs.LastExecutionTimeUtc DESC, rs.RuntimeStatsIntervalId DESC, rs.LastRuntimeStatsId DESC
                ) AS LastValueRank
            FROM CollapsedRuntimeStats AS rs
        ),
        AggregatedRuntimeStats AS
        (
            SELECT
                rs.PlanId,
                SUM(rs.RegularExecutionCount) AS RegularExecutionCount,
                CONVERT(decimal(38, 0), SUM(rs.TotalDurationMicroseconds)) AS TotalDurationMicroseconds,
                CONVERT(decimal(38, 4), SUM(rs.TotalDurationMicroseconds) / NULLIF(CONVERT(decimal(38, 4), SUM(rs.RegularExecutionCount)), CONVERT(decimal(38, 4), 0))) AS AverageDurationMicroseconds,
                MIN(rs.MinimumDurationMicroseconds) AS MinimumDurationMicroseconds,
                MAX(rs.MaximumDurationMicroseconds) AS MaximumDurationMicroseconds,
                MAX(CASE WHEN rs.LastValueRank = 1 THEN rs.LastDurationMicroseconds END) AS LastDurationMicroseconds,
                CONVERT(decimal(38, 0), SUM(rs.TotalCpuMicroseconds)) AS TotalCpuMicroseconds,
                CONVERT(decimal(38, 4), SUM(rs.TotalCpuMicroseconds) / NULLIF(CONVERT(decimal(38, 4), SUM(rs.RegularExecutionCount)), CONVERT(decimal(38, 4), 0))) AS AverageCpuMicroseconds,
                CONVERT(decimal(38, 0), SUM(rs.TotalLogicalReads)) AS TotalLogicalReads,
                CONVERT(decimal(38, 4), SUM(rs.TotalLogicalReads) / NULLIF(CONVERT(decimal(38, 4), SUM(rs.RegularExecutionCount)), CONVERT(decimal(38, 4), 0))) AS AverageLogicalReads,
                CONVERT(decimal(38, 0), SUM(rs.TotalPhysicalReads)) AS TotalPhysicalReads,
                CONVERT(decimal(38, 4), SUM(rs.TotalPhysicalReads) / NULLIF(CONVERT(decimal(38, 4), SUM(rs.RegularExecutionCount)), CONVERT(decimal(38, 4), 0))) AS AveragePhysicalReads,
                CONVERT(decimal(38, 0), SUM(rs.TotalLogicalWrites)) AS TotalLogicalWrites,
                CONVERT(decimal(38, 4), SUM(rs.TotalLogicalWrites) / NULLIF(CONVERT(decimal(38, 4), SUM(rs.RegularExecutionCount)), CONVERT(decimal(38, 4), 0))) AS AverageLogicalWrites,
                CONVERT(decimal(38, 4), SUM(rs.TotalRowCount) / NULLIF(CONVERT(decimal(38, 4), SUM(rs.RegularExecutionCount)), CONVERT(decimal(38, 4), 0))) AS AverageRowCount,
                MAX(rs.MaximumRowCount) AS MaximumRowCount,
                MIN(rs.FirstExecutionTimeUtc) AS FirstExecutionTimeUtc,
                MAX(rs.LastExecutionTimeUtc) AS LastExecutionTimeUtc
            FROM RankedCollapsedRuntimeStats AS rs
            GROUP BY rs.PlanId
            HAVING SUM(rs.RegularExecutionCount) >= @MinExecutions
        ),
        WaitStatsRows AS
        (
            SELECT
                ws.plan_id AS PlanId,
                ws.wait_category_desc AS WaitCategoryDescription,
                CONVERT(decimal(38, 0), ws.total_query_wait_time_ms) AS TotalWaitMilliseconds,
                CONVERT(decimal(38, 0), ws.max_query_wait_time_ms) AS MaximumWaitMilliseconds
            FROM sys.query_store_wait_stats AS ws
            INNER JOIN sys.query_store_runtime_stats_interval AS rsi
                ON rsi.runtime_stats_interval_id = ws.runtime_stats_interval_id
            WHERE @WaitStatsStatus = 'Available'
              AND ws.execution_type = 0
              AND rsi.start_time < @ResolvedEndTime
              AND rsi.end_time > @ResolvedStartTime
        ),
        WaitCategories AS
        (
            SELECT
                ws.PlanId,
                ws.WaitCategoryDescription,
                CONVERT(decimal(38, 0), SUM(ws.TotalWaitMilliseconds)) AS TotalWaitMilliseconds,
                MAX(ws.MaximumWaitMilliseconds) AS MaximumWaitMilliseconds
            FROM WaitStatsRows AS ws
            GROUP BY
                ws.PlanId,
                ws.WaitCategoryDescription
        ),
        AggregatedWaitStats AS
        (
            SELECT
                ws.PlanId,
                CONVERT(decimal(38, 0), SUM(ws.TotalWaitMilliseconds)) AS TotalWaitMilliseconds
            FROM WaitCategories AS ws
            GROUP BY ws.PlanId
        ),
        WaitStatsXml AS
        (
            SELECT
                wp.PlanId,
                (
                    SELECT
                        wc.WaitCategoryDescription AS [@Category],
                        wc.TotalWaitMilliseconds AS [@TotalWaitMilliseconds],
                        CONVERT(decimal(38, 4), wc.TotalWaitMilliseconds / NULLIF(CONVERT(decimal(38, 4), ars.RegularExecutionCount), CONVERT(decimal(38, 4), 0))) AS [@AverageWaitMilliseconds],
                        wc.MaximumWaitMilliseconds AS [@MaximumWaitMilliseconds]
                    FROM WaitCategories AS wc
                    WHERE wc.PlanId = wp.PlanId
                      AND wc.TotalWaitMilliseconds > 0
                    ORDER BY
                        wc.TotalWaitMilliseconds DESC,
                        wc.WaitCategoryDescription ASC
                    FOR XML PATH(N'WaitCategory'), ROOT(N'WaitStats'), TYPE
                ) AS WaitStatsXml
            FROM (SELECT DISTINCT PlanId FROM WaitCategories) AS wp
            INNER JOIN AggregatedRuntimeStats AS ars
                ON ars.PlanId = wp.PlanId
        )
        SELECT
            q.query_id AS QueryId,
            p.plan_id AS PlanId,
            q.query_hash AS QueryHash,
            p.query_plan_hash AS QueryPlanHash,
            qt.query_sql_text AS QuerySqlText,
            q.object_id AS ObjectId,
            OBJECT_NAME(q.object_id) AS ObjectName,
            ars.RegularExecutionCount,
            ars.TotalDurationMicroseconds,
            ars.AverageDurationMicroseconds,
            ars.MinimumDurationMicroseconds,
            ars.MaximumDurationMicroseconds,
            ars.LastDurationMicroseconds,
            ars.TotalCpuMicroseconds,
            ars.AverageCpuMicroseconds,
            ars.TotalLogicalReads,
            ars.AverageLogicalReads,
            ars.TotalPhysicalReads,
            ars.AveragePhysicalReads,
            ars.TotalLogicalWrites,
            ars.AverageLogicalWrites,
            ars.AverageRowCount,
            ars.MaximumRowCount,
            ars.FirstExecutionTimeUtc,
            ars.LastExecutionTimeUtc,
            q.count_compiles AS QueryLevelCompileCount,
            SWITCHOFFSET(q.last_compile_start_time, '+00:00') AS QueryLevelLastCompileTimeUtc,
            p.is_forced_plan AS IsForcedPlan,
            p.force_failure_count AS ForceFailureCount,
            p.last_force_failure_reason AS LastForceFailureReason,
            p.last_force_failure_reason_desc AS LastForceFailureReasonDescription,
            p.plan_group_id AS PlanGroupId,
            p.engine_version AS EngineVersion,
            p.compatibility_level AS CompatibilityLevel,
            p.is_online_index_plan AS IsOnlineIndexPlan,
            p.is_trivial_plan AS IsTrivialPlan,
            p.is_parallel_plan AS IsParallelPlan,
            CASE
                WHEN @WaitStatsStatus = 'Available' THEN ISNULL(aws.TotalWaitMilliseconds, CONVERT(decimal(38, 0), 0))
            END AS TotalWaitMilliseconds,
            CASE
                WHEN @WaitStatsStatus = 'Available' THEN CONVERT(decimal(38, 4), ISNULL(aws.TotalWaitMilliseconds, CONVERT(decimal(38, 0), 0)) / NULLIF(CONVERT(decimal(38, 4), ars.RegularExecutionCount), CONVERT(decimal(38, 4), 0)))
            END AS AverageWaitMilliseconds,
            @WaitStatsStatus AS WaitStatsStatus,
            CASE
                WHEN @WaitStatsStatus = 'Available' THEN ISNULL(wsx.WaitStatsXml, CONVERT(xml, N'<WaitStats />'))
            END AS WaitStatsXml
        FROM AggregatedRuntimeStats AS ars
        INNER JOIN sys.query_store_plan AS p
            ON p.plan_id = ars.PlanId
        INNER JOIN sys.query_store_query AS q
            ON q.query_id = p.query_id
        INNER JOIN sys.query_store_query_text AS qt
            ON qt.query_text_id = q.query_text_id
        LEFT JOIN AggregatedWaitStats AS aws
            ON aws.PlanId = ars.PlanId
        LEFT JOIN WaitStatsXml AS wsx
            ON wsx.PlanId = ars.PlanId
        WHERE ISNULL(q.object_id, -1) <> ISNULL(@SlowQueriesProcedureObjectId, -2)
          AND ISNULL(q.object_id, -1) <> ISNULL(@PlanDiagnosticsProcedureObjectId, -2)
          AND ISNULL(q.object_id, -1) <> ISNULL(@StatisticsHealthProcedureObjectId, -2)
          AND (@QueryTextContains IS NULL OR qt.query_sql_text LIKE @QueryTextPattern ESCAPE N'~')
        ORDER BY
            CASE WHEN @OrderByNormalized = 'TotalWait' AND @WaitStatsStatus = 'Available' AND aws.TotalWaitMilliseconds IS NULL THEN 1 ELSE 0 END ASC,
            CASE WHEN @OrderByNormalized = 'TotalDuration' THEN ars.TotalDurationMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'AverageDuration' THEN ars.AverageDurationMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'MaximumDuration' THEN ars.MaximumDurationMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'TotalCpu' THEN ars.TotalCpuMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'AverageCpu' THEN ars.AverageCpuMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'LogicalReads' THEN ars.TotalLogicalReads END DESC,
            CASE WHEN @OrderByNormalized = 'Executions' THEN ars.RegularExecutionCount END DESC,
            CASE WHEN @OrderByNormalized = 'TotalWait' AND @WaitStatsStatus = 'Available' THEN ISNULL(aws.TotalWaitMilliseconds, CONVERT(decimal(38, 0), 0)) END DESC,
            q.query_id ASC,
            p.plan_id ASC
        OFFSET @Offset ROWS FETCH NEXT @Top ROWS ONLY;

        SET @RowsReturned = @@ROWCOUNT;

        EXECUTE dbo.LogEvent
            @Process = @ProcedureName,
            @Mode = @AuditMode,
            @Status = 'End',
            @Rows = @RowsReturned,
            @Start = @AuditStartTime,
            @Text = @AuditText;
    END TRY
    BEGIN CATCH
        SET @AuditText = CONCAT(
            @AuditText,
            N';ErrorNumber=', ERROR_NUMBER(),
            N';ErrorState=', ERROR_STATE());

        EXECUTE dbo.LogEvent
            @Process = @ProcedureName,
            @Mode = @AuditMode,
            @Status = 'Error',
            @Start = @AuditStartTime,
            @Text = @AuditText;

        THROW;
    END CATCH
END
GO
