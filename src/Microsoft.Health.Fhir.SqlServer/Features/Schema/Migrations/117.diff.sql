IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'FhirDiagnosticsReader'
      AND type = 'R'
)
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.database_principals
        WHERE name = N'FhirDiagnosticsReader'
    )
    BEGIN
        THROW 50100, 'A database principal named FhirDiagnosticsReader already exists but is not a database role.', 1;
    END

    CREATE ROLE [FhirDiagnosticsReader];
END
GO

CREATE OR ALTER PROCEDURE dbo.GetQueryStoreSlowQueries
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
        @QueryStoreReadOnlyReason = readonly_reason
    FROM sys.database_query_store_options;

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
        N';QueryStoreReadOnlyReason=', ISNULL(CONVERT(nvarchar(20), @QueryStoreReadOnlyReason), N'Unknown'));

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
            p.is_parallel_plan AS IsParallelPlan
        FROM AggregatedRuntimeStats AS ars
        INNER JOIN sys.query_store_plan AS p
            ON p.plan_id = ars.PlanId
        INNER JOIN sys.query_store_query AS q
            ON q.query_id = p.query_id
        INNER JOIN sys.query_store_query_text AS qt
            ON qt.query_text_id = q.query_text_id
        WHERE ISNULL(q.object_id, -1) <> ISNULL(@SlowQueriesProcedureObjectId, -2)
          AND ISNULL(q.object_id, -1) <> ISNULL(@PlanDiagnosticsProcedureObjectId, -2)
          AND ISNULL(q.object_id, -1) <> ISNULL(@StatisticsHealthProcedureObjectId, -2)
          AND (@QueryTextContains IS NULL OR qt.query_sql_text LIKE @QueryTextPattern ESCAPE N'~')
        ORDER BY
            CASE WHEN @OrderByNormalized = 'TotalDuration' THEN ars.TotalDurationMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'AverageDuration' THEN ars.AverageDurationMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'MaximumDuration' THEN ars.MaximumDurationMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'TotalCpu' THEN ars.TotalCpuMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'AverageCpu' THEN ars.AverageCpuMicroseconds END DESC,
            CASE WHEN @OrderByNormalized = 'LogicalReads' THEN ars.TotalLogicalReads END DESC,
            CASE WHEN @OrderByNormalized = 'Executions' THEN ars.RegularExecutionCount END DESC,
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

CREATE OR ALTER PROCEDURE dbo.GetQueryStorePlanDiagnostics @PlanId bigint
WITH EXECUTE AS 'dbo'
AS
BEGIN
    SET NOCOUNT ON

    DECLARE @SP varchar(100) = OBJECT_NAME(@@PROCID)
           ,@Mode varchar(200) = 'QueryStorePlanDiagnostics'
           ,@Start datetime = GETUTCDATE()
           ,@Rows int = 0
           ,@QueryStoreState nvarchar(60)
           ,@AuditText nvarchar(3500)
           ,@FoundPlanId bigint
           ,@QueryId bigint
           ,@QueryHash binary(8)
           ,@QueryPlanHash binary(8)
           ,@QuerySqlText nvarchar(max)
           ,@ObjectId int
           ,@PlanGroupId bigint
           ,@EngineVersion nvarchar(128)
           ,@CompatibilityLevel smallint
           ,@IsOnlineIndexPlan bit
           ,@IsTrivialPlan bit
           ,@IsParallelPlan bit
           ,@IsForcedPlan bit
           ,@ForceFailureCount bigint
           ,@LastForceFailureReason int
           ,@LastForceFailureReasonDesc nvarchar(256)
           ,@CountCompiles bigint
           ,@InitialCompileStartTime datetimeoffset(7)
           ,@LastCompileStartTime datetimeoffset(7)
           ,@LastPlanExecutionTime datetimeoffset(7)
           ,@AverageCompileDuration float
           ,@LastCompileDuration bigint
           ,@FirstExecutionTime datetimeoffset(7)
           ,@LastRuntimeExecutionTime datetimeoffset(7)
           ,@RawQueryPlan nvarchar(max)
           ,@LocalPlanXml xml
           ,@SanitizedShowPlanXml xml
           ,@SerializedPlanXml nvarchar(max)
           ,@RemainingParameterListCount bigint
           ,@ForbiddenAttributeCount bigint
           ,@SanitizationStatus varchar(32)
           ,@SanitizationErrorCode varchar(64)
           ,@SerializedResultSizeBytes bigint = 0
           ,@CaughtErrorNumber int
           ,@CaughtErrorState int

    SET @AuditText = CONCAT(
        N'OriginalLogin=', CONVERT(nvarchar(128), ORIGINAL_LOGIN()),
        N';EffectivePrincipal=', CONVERT(nvarchar(128), USER_NAME()),
        N';PlanId=', ISNULL(CONVERT(nvarchar(20), @PlanId), N'NULL'))

    BEGIN TRY
        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Start',@Text=@AuditText

        IF @PlanId IS NULL OR @PlanId <= 0
            THROW 50001, 'Plan ID must be a positive bigint.', 1

        SELECT @QueryStoreState = actual_state_desc
        FROM sys.database_query_store_options

        IF @QueryStoreState IS NULL OR @QueryStoreState NOT IN ('READ_WRITE', 'READ_ONLY')
            THROW 50002, 'Query Store is not readable.', 1

        SELECT @FoundPlanId = p.plan_id
              ,@QueryId = p.query_id
              ,@QueryHash = q.query_hash
              ,@QueryPlanHash = p.query_plan_hash
              ,@QuerySqlText = qt.query_sql_text
              ,@ObjectId = q.object_id
              ,@PlanGroupId = p.plan_group_id
              ,@EngineVersion = p.engine_version
              ,@CompatibilityLevel = p.compatibility_level
              ,@IsOnlineIndexPlan = p.is_online_index_plan
              ,@IsTrivialPlan = p.is_trivial_plan
              ,@IsParallelPlan = p.is_parallel_plan
              ,@IsForcedPlan = p.is_forced_plan
              ,@ForceFailureCount = p.force_failure_count
              ,@LastForceFailureReason = p.last_force_failure_reason
              ,@LastForceFailureReasonDesc = p.last_force_failure_reason_desc
              ,@CountCompiles = p.count_compiles
              ,@InitialCompileStartTime = p.initial_compile_start_time
              ,@LastCompileStartTime = p.last_compile_start_time
              ,@LastPlanExecutionTime = p.last_execution_time
              ,@AverageCompileDuration = p.avg_compile_duration
              ,@LastCompileDuration = p.last_compile_duration
              ,@RawQueryPlan = CONVERT(nvarchar(max), p.query_plan)
        FROM sys.query_store_plan AS p
        LEFT JOIN sys.query_store_query AS q
            ON q.query_id = p.query_id
        LEFT JOIN sys.query_store_query_text AS qt
            ON qt.query_text_id = q.query_text_id
        WHERE p.plan_id = @PlanId

        IF @FoundPlanId IS NULL
            THROW 50003, 'The requested Query Store plan was not found or is no longer retained.', 1

        SELECT @FirstExecutionTime = MIN(first_execution_time)
              ,@LastRuntimeExecutionTime = MAX(last_execution_time)
        FROM sys.query_store_runtime_stats
        WHERE plan_id = @PlanId

        IF @RawQueryPlan IS NULL
        BEGIN
            SET @SanitizationStatus = 'PlanXmlUnavailable'
            SET @SanitizationErrorCode = 'PLAN_XML_UNAVAILABLE'
        END
        ELSE
        BEGIN
            SET @LocalPlanXml = TRY_CONVERT(xml, @RawQueryPlan)

            IF @LocalPlanXml IS NULL
            BEGIN
                SET @SanitizationStatus = 'InvalidXml'
                SET @SanitizationErrorCode = 'PLAN_XML_INVALID'
            END
            ELSE
            BEGIN
                BEGIN TRY
                    SET @SanitizedShowPlanXml = @LocalPlanXml
                    SET @SanitizedShowPlanXml.modify('delete //*[local-name(.) = "ParameterList"]')

                    SET @RemainingParameterListCount = @SanitizedShowPlanXml.value('count(//*[local-name(.) = "ParameterList"])', 'bigint')
                    SET @ForbiddenAttributeCount = @SanitizedShowPlanXml.value('count(//@*[local-name(.) = "ParameterCompiledValue" or local-name(.) = "ParameterRuntimeValue"])', 'bigint')
                    SET @SerializedPlanXml = CONVERT(nvarchar(max), @SanitizedShowPlanXml)

                    IF @RemainingParameterListCount = 0
                        AND @ForbiddenAttributeCount = 0
                        AND CHARINDEX(N'PARAMETERLIST', UPPER(@SerializedPlanXml)) = 0
                        AND CHARINDEX(N'PARAMETERCOMPILEDVALUE', UPPER(@SerializedPlanXml)) = 0
                        AND CHARINDEX(N'PARAMETERRUNTIMEVALUE', UPPER(@SerializedPlanXml)) = 0
                    BEGIN
                        SET @SanitizationStatus = 'Sanitized'
                    END
                    ELSE
                    BEGIN
                        SET @SanitizedShowPlanXml = NULL
                        SET @SerializedPlanXml = NULL
                        SET @SanitizationStatus = 'VerificationFailed'
                        SET @SanitizationErrorCode = 'PLAN_XML_VERIFICATION_FAILED'
                    END
                END TRY
                BEGIN CATCH
                    SET @SanitizedShowPlanXml = NULL
                    SET @SerializedPlanXml = NULL
                    SET @SanitizationStatus = 'VerificationFailed'
                    SET @SanitizationErrorCode = 'PLAN_XML_VERIFICATION_FAILED'
                END CATCH
            END
        END

        SET @SerializedResultSizeBytes = ISNULL(DATALENGTH(@SerializedPlanXml), 0)
        SET @AuditText = CONCAT(
            @AuditText,
            N';SanitizationStatus=', ISNULL(CONVERT(nvarchar(32), @SanitizationStatus), N'NotStarted'),
            N';SanitizationErrorCode=', ISNULL(CONVERT(nvarchar(64), @SanitizationErrorCode), N'NONE'),
            N';SerializedResultSizeBytes=', CONVERT(nvarchar(20), @SerializedResultSizeBytes))

        IF @SanitizationErrorCode IS NOT NULL
            EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Text=@AuditText

        SET @Rows = 1

        SELECT @FoundPlanId AS PlanId
              ,@QueryId AS QueryId
              ,@QueryHash AS QueryHash
              ,@QueryPlanHash AS QueryPlanHash
              ,@QuerySqlText AS QuerySqlText
              ,@ObjectId AS ObjectId
              ,@PlanGroupId AS PlanGroupId
              ,@EngineVersion AS EngineVersion
              ,@CompatibilityLevel AS CompatibilityLevel
              ,@CountCompiles AS CompileCount
              ,@InitialCompileStartTime AS InitialCompileStartTime
              ,@LastCompileStartTime AS LastCompileStartTime
              ,@AverageCompileDuration AS AverageCompileDurationMicroseconds
              ,@LastCompileDuration AS LastCompileDurationMicroseconds
              ,@IsOnlineIndexPlan AS IsOnlineIndexPlan
              ,@IsTrivialPlan AS IsTrivialPlan
              ,@IsParallelPlan AS IsParallelPlan
              ,@IsForcedPlan AS IsForcedPlan
              ,@ForceFailureCount AS ForceFailureCount
              ,@LastForceFailureReason AS LastForceFailureReason
              ,@LastForceFailureReasonDesc AS LastForceFailureReasonDescription
              ,@FirstExecutionTime AS FirstExecutionTime
              ,COALESCE(@LastRuntimeExecutionTime, @LastPlanExecutionTime) AS LastExecutionTime
              ,@SanitizationStatus AS SanitizationStatus
              ,@SanitizationErrorCode AS SanitizationErrorCode
              ,@SanitizedShowPlanXml AS SanitizedShowPlanXml

        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='End',@Start=@Start,@Rows=@Rows,@Text=@AuditText
    END TRY
    BEGIN CATCH
        SET @CaughtErrorNumber = ERROR_NUMBER()
        SET @CaughtErrorState = ERROR_STATE()
        SET @AuditText = CONCAT(
            N'OriginalLogin=', CONVERT(nvarchar(128), ORIGINAL_LOGIN()),
            N';EffectivePrincipal=', CONVERT(nvarchar(128), USER_NAME()),
            N';PlanId=', ISNULL(CONVERT(nvarchar(20), @PlanId), N'NULL'),
            N';SanitizationStatus=', ISNULL(CONVERT(nvarchar(32), @SanitizationStatus), N'NotCompleted'),
            N';SanitizationErrorCode=PLAN_DIAGNOSTICS_FAILED',
            N';ErrorNumber=', CONVERT(nvarchar(11), @CaughtErrorNumber),
            N';ErrorState=', CONVERT(nvarchar(11), @CaughtErrorState))

        EXECUTE dbo.LogEvent @Process=@SP,@Mode=@Mode,@Status='Error',@Start=@Start,@Text=@AuditText;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.GetStatisticsHealth
    @TableName nvarchar(128) = NULL,
    @Top int = 20,
    @Offset int = 0,
    @OrderBy varchar(32) = 'ModificationPercent'
WITH EXECUTE AS 'dbo'
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @SP varchar(100) = OBJECT_NAME(@@PROCID);
    DECLARE @Mode varchar(200) = 'StatisticsHealth';
    DECLARE @AuditText nvarchar(3500) = CONCAT(
        N'OriginalLogin=', CONVERT(nvarchar(128), ORIGINAL_LOGIN()),
        N';EffectivePrincipal=', CONVERT(nvarchar(128), USER_NAME()),
        N';TableNameSupplied=', CASE WHEN @TableName IS NULL THEN N'0' ELSE N'1' END,
        N';Top=', ISNULL(CONVERT(nvarchar(11), @Top), N'NULL'),
        N';Offset=', ISNULL(CONVERT(nvarchar(11), @Offset), N'NULL'));
    DECLARE @Start datetime = GETUTCDATE();
    DECLARE @Rows int;
    DECLARE @TableObjectId int;
    DECLARE @TableCount int;
    DECLARE @NormalizedOrderBy varchar(32) = UPPER(@OrderBy COLLATE Latin1_General_100_CI_AS);
    DECLARE @CaughtErrorNumber int;
    DECLARE @CaughtErrorState int;

    BEGIN TRY
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Start', @Text = @AuditText;

        IF @Top IS NULL OR @Top < 1 OR @Top > 100
        BEGIN
            THROW 50000, '@Top must be between 1 and 100.', 127;
        END

        IF @Offset IS NULL OR @Offset < 0 OR @Offset > 10000
        BEGIN
            THROW 50000, '@Offset must be between 0 and 10000.', 127;
        END

        IF @NormalizedOrderBy IS NULL
            OR @NormalizedOrderBy NOT IN ('MODIFICATIONCOUNT', 'MODIFICATIONPERCENT', 'LASTUPDATED', 'SAMPLINGPERCENT', 'ROWS')
        BEGIN
            THROW 50000, '@OrderBy must be ModificationCount, ModificationPercent, LastUpdated, SamplingPercent, or Rows.', 127;
        END

        IF @TableName IS NOT NULL
        BEGIN
            IF LEN(LTRIM(RTRIM(@TableName))) = 0
            BEGIN
                THROW 50000, '@TableName must be nonblank when supplied.', 127;
            END

            SELECT
                @TableCount = COUNT(*),
                @TableObjectId = MIN(tableInfo.object_id)
            FROM sys.tables AS tableInfo
            WHERE tableInfo.is_ms_shipped = 0
                AND tableInfo.name COLLATE DATABASE_DEFAULT = @TableName COLLATE DATABASE_DEFAULT;

            IF @TableCount = 0
            BEGIN
                THROW 50000, '@TableName does not resolve to a user table.', 127;
            END

            IF @TableCount > 1
            BEGIN
                THROW 50000, '@TableName must resolve to exactly one user table.', 127;
            END
        END

        SET @AuditText = CONCAT(
            @AuditText,
            N';TableName=', ISNULL(@TableName, N'NULL'),
            N';OrderBy=', @NormalizedOrderBy);

        ;WITH StatisticsMetadata AS
        (
            SELECT
                tableInfo.name AS TableName,
                statisticsInfo.name AS StatisticsName,
                statisticsInfo.stats_id AS StatisticsId,
                (
                    SELECT
                        statisticsColumn.stats_column_id AS [@Ordinal],
                        columnInfo.name AS [@Name]
                    FROM sys.stats_columns AS statisticsColumn
                    INNER JOIN sys.columns AS columnInfo
                        ON columnInfo.object_id = statisticsColumn.object_id
                        AND columnInfo.column_id = statisticsColumn.column_id
                    WHERE statisticsColumn.object_id = statisticsInfo.object_id
                        AND statisticsColumn.stats_id = statisticsInfo.stats_id
                    ORDER BY statisticsColumn.stats_column_id
                    FOR XML PATH('StatisticsColumn'), ROOT('StatisticsColumns'), TYPE
                ) AS StatisticsColumns,
                statisticsInfo.auto_created AS AutoCreated,
                statisticsInfo.user_created AS UserCreated,
                statisticsInfo.is_incremental AS IsIncremental,
                statisticsInfo.has_persisted_sample AS HasPersistedSample,
                statisticsInfo.no_recompute AS NoRecompute,
                statisticsInfo.has_filter AS HasFilter,
                statisticsInfo.filter_definition AS FilterDefinition,
                indexInfo.index_id AS IndexId,
                indexInfo.name AS IndexName,
                indexInfo.type_desc AS IndexTypeDescription,
                indexInfo.is_disabled AS IsIndexDisabled,
                indexInfo.is_hypothetical AS IsIndexHypothetical,
                statisticsProperties.last_updated AS LastUpdated,
                CONVERT(decimal(38, 4),
                    CONVERT(decimal(38, 0), DATEDIFF_BIG(SECOND, statisticsProperties.last_updated, SYSUTCDATETIME()))
                    / CONVERT(decimal(4, 0), 3600)) AS HoursSinceLastUpdate,
                statisticsProperties.rows AS [Rows],
                statisticsProperties.unfiltered_rows AS UnfilteredRows,
                statisticsProperties.rows_sampled AS RowsSampled,
                CONVERT(decimal(38, 4),
                    (CONVERT(decimal(38, 0), statisticsProperties.rows_sampled) * CONVERT(decimal(3, 0), 100))
                    / NULLIF(CONVERT(decimal(38, 0), statisticsProperties.rows), CONVERT(decimal(38, 0), 0))) AS SamplingPercent,
                statisticsProperties.steps AS HistogramStepCount,
                statisticsProperties.modification_counter AS ModificationCount,
                CONVERT(decimal(38, 4),
                    (CONVERT(decimal(38, 0), statisticsProperties.modification_counter) * CONVERT(decimal(3, 0), 100))
                    / NULLIF(CONVERT(decimal(38, 0), statisticsProperties.rows), CONVERT(decimal(38, 0), 0))) AS ModificationPercent,
                CASE
                    WHEN statisticsProperties.PropertiesAvailable IS NULL THEN 'PropertiesUnavailable'
                    ELSE 'Available'
                END AS StatisticsStatus
            FROM sys.tables AS tableInfo
            INNER JOIN sys.stats AS statisticsInfo
                ON statisticsInfo.object_id = tableInfo.object_id
            LEFT JOIN sys.indexes AS indexInfo
                ON indexInfo.object_id = statisticsInfo.object_id
                AND indexInfo.index_id = statisticsInfo.stats_id
            OUTER APPLY
            (
                SELECT
                    1 AS PropertiesAvailable,
                    properties.last_updated,
                    properties.rows,
                    properties.rows_sampled,
                    properties.steps,
                    properties.unfiltered_rows,
                    properties.modification_counter
                FROM sys.dm_db_stats_properties(statisticsInfo.object_id, statisticsInfo.stats_id) AS properties
            ) AS statisticsProperties
            WHERE tableInfo.is_ms_shipped = 0
                AND
                (
                    (@TableName IS NOT NULL AND tableInfo.object_id = @TableObjectId)
                    OR
                    (@TableName IS NULL AND tableInfo.temporal_type <> 1)
                )
        ),
        OrderedStatisticsMetadata AS
        (
            SELECT
                *,
                ROW_NUMBER() OVER
                (
                    ORDER BY
                        CASE WHEN @NormalizedOrderBy = 'MODIFICATIONCOUNT' THEN ModificationCount END DESC,
                        CASE WHEN @NormalizedOrderBy = 'MODIFICATIONPERCENT' THEN ModificationPercent END DESC,
                        CASE WHEN @NormalizedOrderBy = 'LASTUPDATED' AND LastUpdated IS NULL THEN 0
                             WHEN @NormalizedOrderBy = 'LASTUPDATED' THEN 1
                        END ASC,
                        CASE WHEN @NormalizedOrderBy = 'LASTUPDATED' THEN LastUpdated END ASC,
                        CASE WHEN @NormalizedOrderBy = 'SAMPLINGPERCENT' THEN SamplingPercent END DESC,
                        CASE WHEN @NormalizedOrderBy = 'ROWS' THEN [Rows] END DESC,
                        TableName ASC,
                        StatisticsName ASC,
                        StatisticsId ASC
                ) AS RowNumber
            FROM StatisticsMetadata
        )
        SELECT
            TableName,
            StatisticsName,
            StatisticsId,
            StatisticsColumns,
            AutoCreated,
            UserCreated,
            IsIncremental,
            HasPersistedSample,
            NoRecompute,
            HasFilter,
            FilterDefinition,
            IndexId,
            IndexName,
            IndexTypeDescription,
            IsIndexDisabled,
            IsIndexHypothetical,
            LastUpdated,
            HoursSinceLastUpdate,
            [Rows],
            UnfilteredRows,
            RowsSampled,
            SamplingPercent,
            HistogramStepCount,
            ModificationCount,
            ModificationPercent,
            StatisticsStatus
        FROM OrderedStatisticsMetadata
        WHERE RowNumber > @Offset
            AND RowNumber <= @Offset + @Top
        ORDER BY RowNumber;

        SET @Rows = @@ROWCOUNT;

        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'End', @Start = @Start, @Rows = @Rows, @Text = @AuditText;
    END TRY
    BEGIN CATCH
        SET @CaughtErrorNumber = ERROR_NUMBER();
        SET @CaughtErrorState = ERROR_STATE();
        SET @AuditText = CONCAT(
            N'OriginalLogin=', CONVERT(nvarchar(128), ORIGINAL_LOGIN()),
            N';EffectivePrincipal=', CONVERT(nvarchar(128), USER_NAME()),
            N';StatisticsHealthErrorCode=STATISTICS_HEALTH_FAILED',
            N';ErrorNumber=', CONVERT(nvarchar(11), @CaughtErrorNumber),
            N';ErrorState=', CONVERT(nvarchar(11), @CaughtErrorState));

        IF ERROR_NUMBER() = 1750 THROW;
        EXECUTE dbo.LogEvent @Process = @SP, @Mode = @Mode, @Status = 'Error', @Start = @Start, @Text = @AuditText;
        THROW;
    END CATCH
END
GO

-- ── 3. Grant EXECUTE on each procedure individually to [FhirDiagnosticsReader] -
GRANT EXECUTE ON dbo.GetQueryStoreSlowQueries    TO [FhirDiagnosticsReader];
GRANT EXECUTE ON dbo.GetQueryStorePlanDiagnostics TO [FhirDiagnosticsReader];
GRANT EXECUTE ON dbo.GetStatisticsHealth          TO [FhirDiagnosticsReader];
GO
