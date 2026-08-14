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
