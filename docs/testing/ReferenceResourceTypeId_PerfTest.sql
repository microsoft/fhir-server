-- =====================================================================================
-- ReferenceResourceTypeId WHERE Clause Performance Test
-- =====================================================================================
-- Purpose:  Determine whether adding ReferenceResourceTypeId (with IS NULL handling)
--           to generated SQL WHERE clauses improves index utilization without harming
--           correctness or performance.
--
-- Context:  PR #5285 modifies UntypedReferenceRewriter to add ReferenceResourceTypeId
--           filters. The secondary index on ReferenceSearchParam has column order:
--             (ReferenceResourceId, ReferenceResourceTypeId, SearchParamId, BaseUri,
--              ResourceSurrogateId, ResourceTypeId)
--           Skipping ReferenceResourceTypeId (column 2) limits seek effectiveness.
--           However, ReferenceResourceTypeId is nullable — untyped string references
--           store NULL. We must test whether including IS NULL degrades plans.
--
-- Approach: We test across THREE NULL distribution ratios (10%, 30%, 60%) because
--           production data distribution is unknown. Each ratio gets its own table
--           with identical schema and indexes. All 7 query variants x 4 scenarios
--           are run against each table.
--
-- Usage:    1. Run Part 1 to create the test database and load data (3 tables)
--           2. Run Part 2 to execute all query variants and capture results
--           3. Run Part 3 to review analysis
--
-- Requirements: SQL Server 2019+ (or Azure SQL). sysadmin or dbcreator for Part 1.
-- =====================================================================================

-- =====================================================================================
-- PART 1: DATABASE SETUP AND DATA GENERATION
-- =====================================================================================

USE master;
GO

IF DB_ID('FhirRefTypeIdTest') IS NOT NULL
BEGIN
    ALTER DATABASE FhirRefTypeIdTest SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE FhirRefTypeIdTest;
END
GO

CREATE DATABASE FhirRefTypeIdTest;
GO

USE FhirRefTypeIdTest;
GO

-- =====================================================================================
-- Helper: Create a ReferenceSearchParam table with schema and indexes
-- =====================================================================================
IF OBJECT_ID('dbo.CreateTestTable') IS NOT NULL DROP PROCEDURE dbo.CreateTestTable;
GO

CREATE PROCEDURE dbo.CreateTestTable @TableName SYSNAME
AS
BEGIN
    DECLARE @Sql NVARCHAR(MAX);

    SET @Sql = N'
    CREATE TABLE dbo.' + QUOTENAME(@TableName) + N'
    (
        ResourceTypeId                      smallint                NOT NULL,
        ResourceSurrogateId                 bigint                  NOT NULL,
        SearchParamId                       smallint                NOT NULL,
        BaseUri                             varchar(128)            COLLATE Latin1_General_100_CS_AS NULL,
        ReferenceResourceTypeId             smallint                NULL,
        ReferenceResourceId                 varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
        ReferenceResourceVersion            int                     NULL
    );

    ALTER TABLE dbo.' + QUOTENAME(@TableName) + N' SET ( LOCK_ESCALATION = AUTO );

    CREATE CLUSTERED INDEX IXC_' + @TableName + N'
    ON dbo.' + QUOTENAME(@TableName) + N' (ResourceTypeId, ResourceSurrogateId, SearchParamId)
    WITH (DATA_COMPRESSION = PAGE);

    CREATE UNIQUE INDEX IXU_' + @TableName + N'
    ON dbo.' + QUOTENAME(@TableName) + N' (
        ReferenceResourceId, ReferenceResourceTypeId, SearchParamId,
        BaseUri, ResourceSurrogateId, ResourceTypeId
    )
    WITH (DATA_COMPRESSION = PAGE);';

    EXEC sp_executesql @Sql;
    PRINT 'Created table: ' + @TableName;
END
GO

-- Create three tables for different NULL ratios
EXEC dbo.CreateTestTable @TableName = 'RefSearch_Null10';   -- 10% NULL
EXEC dbo.CreateTestTable @TableName = 'RefSearch_Null30';   -- 30% NULL
EXEC dbo.CreateTestTable @TableName = 'RefSearch_Null60';   -- 60% NULL
GO

-- =====================================================================================
-- Helper: Populate a table with test data at a given NULL percentage
-- =====================================================================================
IF OBJECT_ID('dbo.PopulateTestTable') IS NOT NULL DROP PROCEDURE dbo.PopulateTestTable;
GO

CREATE PROCEDURE dbo.PopulateTestTable
    @TableName      SYSNAME,
    @NullPercent    INT,            -- 0-100: percentage of rows with NULL ReferenceResourceTypeId
    @TotalRows      INT = 2000000
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BatchSize      INT = 50000;
    DECLARE @CurrentBatch   INT = 0;
    DECLARE @SurrogateBase  BIGINT = 100000000000;
    DECLARE @NonNullPct     INT = 100 - @NullPercent;

    -- Typed distribution (proportional within the non-null slice):
    --   Patient=103 (44%), Practitioner=104 (17%), Organization=105 (11%),
    --   Device=106 (6%), Group=107 (6%), Location=108 (6%), RelatedPerson=109 (5%), Medication=110 (5%)
    -- These sum to 100% of the non-null portion.
    -- Thresholds: we scale these into [0, @NonNullPct) of the full 0-99 range.
    DECLARE @PatientCutoff       INT = @NonNullPct * 44 / 100;  -- ~44% of non-null
    DECLARE @PractitionerCutoff  INT = @PatientCutoff  + @NonNullPct * 17 / 100;
    DECLARE @OrgCutoff           INT = @PractitionerCutoff + @NonNullPct * 11 / 100;
    DECLARE @DeviceCutoff        INT = @OrgCutoff      + @NonNullPct *  6 / 100;
    DECLARE @GroupCutoff         INT = @DeviceCutoff    + @NonNullPct *  6 / 100;
    DECLARE @LocationCutoff      INT = @GroupCutoff     + @NonNullPct *  6 / 100;
    DECLARE @RelPersonCutoff     INT = @LocationCutoff  + @NonNullPct *  5 / 100;
    -- Anything from @RelPersonCutoff to @NonNullPct-1 = Medication (110)
    -- Anything from @NonNullPct to 99 = NULL

    -- Resource types + search params: use a temp table so dynamic SQL can access it
    IF OBJECT_ID('tempdb..#ResourceTypes') IS NOT NULL DROP TABLE #ResourceTypes;
    CREATE TABLE #ResourceTypes (ResourceTypeId SMALLINT, SearchParamId SMALLINT, LowBound INT, HighBound INT);
    INSERT INTO #ResourceTypes VALUES
        (40, 414,  0, 24),   -- DiagnosticReport / subject
        (40, 217, 25, 39),   -- DiagnosticReport / clinical-patient
        (57, 414, 40, 59),   -- Observation / subject
        (57, 300, 60, 69),   -- Observation / performer
        (20, 217, 70, 77),   -- Condition / clinical-patient
        (25, 350, 78, 84),   -- Encounter / participant
        (15, 500, 85, 89),   -- Claim / provider
        (50, 550, 90, 94),   -- MedicationRequest / requester
        (25, 450, 95, 99);   -- Encounter / managing-organization

    DECLARE @Sql NVARCHAR(MAX);

    PRINT 'Populating ' + @TableName + ' (' + CAST(@TotalRows AS VARCHAR) + ' rows, '
        + CAST(@NullPercent AS VARCHAR) + '% NULL)...';
    PRINT '  Start: ' + CONVERT(VARCHAR, GETDATE(), 121);

    WHILE @CurrentBatch * @BatchSize < @TotalRows
    BEGIN
        SET @Sql = N'
        ;WITH Numbers AS (
            SELECT TOP (' + CAST(@BatchSize AS NVARCHAR) + N')
                ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
            FROM sys.all_objects a CROSS JOIN sys.all_objects b
        ),
        RandomData AS (
            SELECT N,
                ABS(CHECKSUM(NEWID())) % 100 AS ResourceRand,
                ABS(CHECKSUM(NEWID())) % 100 AS TypeRand,
                ABS(CHECKSUM(NEWID())) % 20000 AS RefIdRand,
                ' + CAST(@SurrogateBase AS NVARCHAR(20)) + N' + ('
                    + CAST(@CurrentBatch AS NVARCHAR) + N' * ' + CAST(@BatchSize AS NVARCHAR) + N') + N AS SurrogateId
            FROM Numbers
        )
        INSERT INTO dbo.' + QUOTENAME(@TableName) + N'
            (ResourceTypeId, ResourceSurrogateId, SearchParamId,
             BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion)
        SELECT
            rt.ResourceTypeId,
            rd.SurrogateId,
            rt.SearchParamId,
            NULL,
            CASE
                WHEN rd.TypeRand < ' + CAST(@PatientCutoff AS NVARCHAR)      + N' THEN 103
                WHEN rd.TypeRand < ' + CAST(@PractitionerCutoff AS NVARCHAR) + N' THEN 104
                WHEN rd.TypeRand < ' + CAST(@OrgCutoff AS NVARCHAR)          + N' THEN 105
                WHEN rd.TypeRand < ' + CAST(@DeviceCutoff AS NVARCHAR)       + N' THEN 106
                WHEN rd.TypeRand < ' + CAST(@GroupCutoff AS NVARCHAR)        + N' THEN 107
                WHEN rd.TypeRand < ' + CAST(@LocationCutoff AS NVARCHAR)     + N' THEN 108
                WHEN rd.TypeRand < ' + CAST(@RelPersonCutoff AS NVARCHAR)    + N' THEN 109
                WHEN rd.TypeRand < ' + CAST(@NonNullPct AS NVARCHAR)         + N' THEN 110
                ELSE NULL
            END,
            CASE
                WHEN rd.RefIdRand < 100  THEN ''common-patient-'' + RIGHT(''0000'' + CAST(rd.RefIdRand % 10 AS VARCHAR), 4)
                WHEN rd.RefIdRand < 500  THEN ''patient-'' + RIGHT(''0000'' + CAST(rd.RefIdRand AS VARCHAR), 5)
                ELSE ''ref-'' + CAST(rd.RefIdRand AS VARCHAR)
            END,
            NULL
        FROM RandomData rd
        CROSS APPLY (
            SELECT TOP 1 ResourceTypeId, SearchParamId
            FROM #ResourceTypes
            WHERE rd.ResourceRand BETWEEN LowBound AND HighBound
        ) rt;';

        EXEC sp_executesql @Sql;

        SET @CurrentBatch = @CurrentBatch + 1;
        IF @CurrentBatch % 10 = 0
            PRINT '  Inserted ' + CAST(@CurrentBatch * @BatchSize AS VARCHAR) + ' rows...';
    END

    -- Seed specific scenario IDs (using surrogate offsets per table to stay unique)
    DECLARE @SeedBase BIGINT = @SurrogateBase + @TotalRows + 1000;

    SET @Sql = N'
    -- S2: Rare ID
    INSERT INTO dbo.' + QUOTENAME(@TableName) + N' VALUES
        (40, ' + CAST(@SeedBase + 1 AS NVARCHAR) + N', 414, NULL, 103, ''rare-singleton-id'', NULL),
        (57, ' + CAST(@SeedBase + 2 AS NVARCHAR) + N', 414, NULL, 103, ''rare-singleton-id'', NULL);
    -- S3: Overlap (typed + NULL)
    INSERT INTO dbo.' + QUOTENAME(@TableName) + N' VALUES
        (40, ' + CAST(@SeedBase + 11 AS NVARCHAR) + N', 414, NULL, 103, ''overlap-typed-null-id'', NULL),
        (40, ' + CAST(@SeedBase + 12 AS NVARCHAR) + N', 414, NULL, 104, ''overlap-typed-null-id'', NULL),
        (57, ' + CAST(@SeedBase + 13 AS NVARCHAR) + N', 414, NULL, NULL, ''overlap-typed-null-id'', NULL),
        (57, ' + CAST(@SeedBase + 14 AS NVARCHAR) + N', 217, NULL, NULL, ''overlap-typed-null-id'', NULL),
        (20, ' + CAST(@SeedBase + 15 AS NVARCHAR) + N', 217, NULL, 103, ''overlap-typed-null-id'', NULL);
    -- S4: NULL only
    INSERT INTO dbo.' + QUOTENAME(@TableName) + N' VALUES
        (40, ' + CAST(@SeedBase + 21 AS NVARCHAR) + N', 414, NULL, NULL, ''null-only-id'', NULL),
        (57, ' + CAST(@SeedBase + 22 AS NVARCHAR) + N', 414, NULL, NULL, ''null-only-id'', NULL),
        (20, ' + CAST(@SeedBase + 23 AS NVARCHAR) + N', 217, NULL, NULL, ''null-only-id'', NULL);';

    EXEC sp_executesql @Sql;

    -- Rebuild indexes and update statistics
    SET @Sql = N'
    ALTER INDEX IXC_' + @TableName + N' ON dbo.' + QUOTENAME(@TableName) + N' REBUILD WITH (DATA_COMPRESSION = PAGE);
    ALTER INDEX IXU_' + @TableName + N' ON dbo.' + QUOTENAME(@TableName) + N' REBUILD WITH (DATA_COMPRESSION = PAGE);
    UPDATE STATISTICS dbo.' + QUOTENAME(@TableName) + N' WITH FULLSCAN;';
    EXEC sp_executesql @Sql;

    -- Report distribution
    SET @Sql = N'
    SELECT ''' + @TableName + N''' AS TableName,
        COUNT(*) AS TotalRows,
        SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END) AS NullRows,
        CAST(100.0 * SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,1)) AS [NullPct],
        SUM(CASE WHEN ReferenceResourceTypeId = 103 THEN 1 ELSE 0 END) AS PatientRows,
        SUM(CASE WHEN ReferenceResourceTypeId = 104 THEN 1 ELSE 0 END) AS PractitionerRows,
        COUNT(DISTINCT ReferenceResourceId) AS DistinctRefIds,
        COUNT(DISTINCT SearchParamId) AS DistinctSearchParams
    FROM dbo.' + QUOTENAME(@TableName) + N';';
    EXEC sp_executesql @Sql;

    PRINT '  Done: ' + CONVERT(VARCHAR, GETDATE(), 121);
END
GO

-- Populate all three tables
EXEC dbo.PopulateTestTable @TableName = 'RefSearch_Null10', @NullPercent = 10;
EXEC dbo.PopulateTestTable @TableName = 'RefSearch_Null30', @NullPercent = 30;
EXEC dbo.PopulateTestTable @TableName = 'RefSearch_Null60', @NullPercent = 60;
GO

-- Verify scenario IDs across all tables
SELECT 'RefSearch_Null10' AS Tbl, ReferenceResourceId, COUNT(*) AS Cnt,
    SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END) AS NullCnt
FROM dbo.RefSearch_Null10
WHERE ReferenceResourceId IN ('common-patient-0001','rare-singleton-id','overlap-typed-null-id','null-only-id')
GROUP BY ReferenceResourceId
UNION ALL
SELECT 'RefSearch_Null30', ReferenceResourceId, COUNT(*),
    SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END)
FROM dbo.RefSearch_Null30
WHERE ReferenceResourceId IN ('common-patient-0001','rare-singleton-id','overlap-typed-null-id','null-only-id')
GROUP BY ReferenceResourceId
UNION ALL
SELECT 'RefSearch_Null60', ReferenceResourceId, COUNT(*),
    SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END)
FROM dbo.RefSearch_Null60
WHERE ReferenceResourceId IN ('common-patient-0001','rare-singleton-id','overlap-typed-null-id','null-only-id')
GROUP BY ReferenceResourceId
ORDER BY 1, 2;
GO

PRINT '=== PART 1 COMPLETE: All 3 tables ready ===';
GO


-- =====================================================================================
-- PART 2: QUERY EXECUTION AND METRICS CAPTURE
-- =====================================================================================

USE FhirRefTypeIdTest;
GO

-- Persistent results table (survives across batches)
IF OBJECT_ID('dbo.TestResults') IS NOT NULL DROP TABLE dbo.TestResults;
CREATE TABLE dbo.TestResults (
    TestId              INT IDENTITY(1,1) PRIMARY KEY,
    NullDistribution    VARCHAR(20),    -- Null10, Null30, Null60
    QueryVariant        VARCHAR(50),    -- Q1-Q7
    Scenario            VARCHAR(10),    -- S1-S4
    Description         VARCHAR(200),
    RowsReturned        INT,
    IndexUsed           VARCHAR(200),
    SeekOrScan          VARCHAR(50),
    EstimatedRows       FLOAT,
    ActualRows          INT,
    ExecutionPlanXml    XML
);
GO

-- =====================================================================================
-- Helper: Run a single query, capture plan + metrics, insert into TestResults
-- =====================================================================================
IF OBJECT_ID('dbo.RunAndCapture') IS NOT NULL DROP PROCEDURE dbo.RunAndCapture;
GO

CREATE PROCEDURE dbo.RunAndCapture
    @NullDistribution   VARCHAR(20),
    @QueryVariant       VARCHAR(50),
    @Scenario           VARCHAR(10),
    @Description        VARCHAR(200),
    @SqlText            NVARCHAR(MAX),
    @RefId              VARCHAR(64)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CountSql NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @SqlText + N') q';
    DECLARE @Params NVARCHAR(MAX) = N'@SearchParamId SMALLINT, @ResourceTypeId SMALLINT, '
        + N'@ReferenceResourceId VARCHAR(64), @ReferenceResourceTypeId SMALLINT, '
        + N'@Type1 SMALLINT, @Type2 SMALLINT, @cnt INT OUTPUT';

    DECLARE @Cnt INT;
    EXEC sp_executesql @CountSql, @Params,
        @SearchParamId = 414, @ResourceTypeId = 40,
        @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103,
        @Type1 = 103, @Type2 = 104,
        @cnt = @Cnt OUTPUT;

    -- Capture execution plan from cache
    DECLARE @PlanXml XML;
    SELECT TOP 1 @PlanXml = qp.query_plan
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    -- Extract index operation info from plan XML
    -- Note: XQuery in SQL Server does not support the '|' (union) operator,
    -- so we query RelOp nodes and filter by PhysicalOp instead.
    -- In showplan XML: RelOp[@PhysicalOp] > IndexScan > Object[@Index]
    DECLARE @IndexName VARCHAR(200) = 'N/A', @SeekOrScan VARCHAR(50) = 'N/A';
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1
        @SeekOrScan = n.value('(@PhysicalOp)[1]','VARCHAR(50)'),
        @IndexName  = ISNULL(
            n.value('(*/Object/@Index)[1]','VARCHAR(200)'),
            'N/A')
    FROM @PlanXml.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN
        ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    -- Extract estimated rows
    DECLARE @EstRows FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @EstRows = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @PlanXml.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN
        ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO dbo.TestResults
        (NullDistribution, QueryVariant, Scenario, Description,
         RowsReturned, IndexUsed, SeekOrScan, EstimatedRows, ActualRows, ExecutionPlanXml)
    VALUES
        (@NullDistribution, @QueryVariant, @Scenario, @Description,
         @Cnt, @IndexName, @SeekOrScan, @EstRows, @Cnt, @PlanXml);

    PRINT @NullDistribution + ' | ' + @QueryVariant + ' | ' + @Scenario
        + ': ' + CAST(@Cnt AS VARCHAR) + ' rows, ' + @SeekOrScan + ' on ' + @IndexName;
END
GO

-- =====================================================================================
-- Helper: Run all 7 query variants x 4 scenarios against a given table
-- =====================================================================================
IF OBJECT_ID('dbo.RunAllTests') IS NOT NULL DROP PROCEDURE dbo.RunAllTests;
GO

CREATE PROCEDURE dbo.RunAllTests
    @TableName          SYSNAME,
    @NullDistribution   VARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Scenarios TABLE (Scenario VARCHAR(10), RefId VARCHAR(64), Descr VARCHAR(100));
    INSERT INTO @Scenarios VALUES
        ('S1', 'common-patient-0001',   'Common ID (many rows)'),
        ('S2', 'rare-singleton-id',     'Rare ID (few rows)'),
        ('S3', 'overlap-typed-null-id', 'Typed+NULL overlap'),
        ('S4', 'null-only-id',          'NULL-only ID');

    DECLARE @Scen VARCHAR(10), @RefId VARCHAR(64), @Desc VARCHAR(100);
    DECLARE @Q NVARCHAR(MAX);

    DECLARE scen_cur CURSOR FAST_FORWARD FOR SELECT Scenario, RefId, Descr FROM @Scenarios;
    OPEN scen_cur;
    FETCH NEXT FROM scen_cur INTO @Scen, @RefId, @Desc;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Q1: Baseline — no ReferenceResourceTypeId filter
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q1-Baseline', @Scen,
            @Desc, @Q, @RefId;

        -- Q2: Single type equality
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceTypeId = @ReferenceResourceTypeId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q2-SingleType', @Scen,
            @Desc, @Q, @RefId;

        -- Q3: Multiple types (OR)
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE (ReferenceResourceTypeId = @Type1 OR ReferenceResourceTypeId = @Type2)'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q3-MultiTypeOR', @Scen,
            @Desc, @Q, @RefId;

        -- Q4: Single type + NULL
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE (ReferenceResourceTypeId = @ReferenceResourceTypeId OR ReferenceResourceTypeId IS NULL)'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q4-SingleTypeNULL', @Scen,
            @Desc, @Q, @RefId;

        -- Q5: Multiple types + NULL
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE (ReferenceResourceTypeId = @Type1 OR ReferenceResourceTypeId = @Type2 OR ReferenceResourceTypeId IS NULL)'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q5-MultiTypeNULL', @Scen,
            @Desc, @Q, @RefId;

        -- Q6: IN clause (no NULL)
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE ReferenceResourceTypeId IN (@Type1, @Type2)'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q6-InClause', @Scen,
            @Desc, @Q, @RefId;

        -- Q7: UNION ALL (typed seek + NULL seek)
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE ReferenceResourceTypeId = @ReferenceResourceTypeId'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId'
            + N' UNION ALL '
            + N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE ReferenceResourceTypeId IS NULL'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q7-UnionAll', @Scen,
            @Desc, @Q, @RefId;

        FETCH NEXT FROM scen_cur INTO @Scen, @RefId, @Desc;
    END

    CLOSE scen_cur;
    DEALLOCATE scen_cur;

    PRINT '';
    PRINT @NullDistribution + ' tests complete.';
END
GO

-- Run tests against all three tables
PRINT '=== Running tests on 10% NULL table ===';
EXEC dbo.RunAllTests @TableName = 'RefSearch_Null10', @NullDistribution = 'Null10';
GO
PRINT '=== Running tests on 30% NULL table ===';
EXEC dbo.RunAllTests @TableName = 'RefSearch_Null30', @NullDistribution = 'Null30';
GO
PRINT '=== Running tests on 60% NULL table ===';
EXEC dbo.RunAllTests @TableName = 'RefSearch_Null60', @NullDistribution = 'Null60';
GO


-- =====================================================================================
-- PART 3: RESULTS ANALYSIS
-- =====================================================================================

PRINT '';
PRINT '=========================================================================';
PRINT ' RESULTS SUMMARY — All distributions, all variants, all scenarios';
PRINT '=========================================================================';

SELECT
    NullDistribution,
    QueryVariant,
    Scenario,
    RowsReturned,
    IndexUsed,
    SeekOrScan,
    CAST(EstimatedRows AS INT) AS EstRows,
    ActualRows,
    Description
FROM dbo.TestResults
ORDER BY NullDistribution, Scenario, QueryVariant;
GO

-- =========================================================================
-- CORRECTNESS CHECK: compare each variant's row count to Q1 baseline
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' CORRECTNESS CHECK — Row count differences vs baseline (Q1)';
PRINT '=========================================================================';

SELECT
    t.NullDistribution,
    t.Scenario,
    t.QueryVariant,
    t.RowsReturned,
    bl.RowsReturned AS BaselineRows,
    t.RowsReturned - bl.RowsReturned AS RowDiff,
    CASE
        WHEN t.RowsReturned < bl.RowsReturned THEN '!! MISSING ROWS'
        WHEN t.RowsReturned > bl.RowsReturned THEN '+ Extra rows'
        ELSE 'OK'
    END AS Status
FROM dbo.TestResults t
JOIN dbo.TestResults bl
    ON  bl.NullDistribution = t.NullDistribution
    AND bl.Scenario         = t.Scenario
    AND bl.QueryVariant     = 'Q1-Baseline'
WHERE t.QueryVariant <> 'Q1-Baseline'
ORDER BY t.NullDistribution, t.Scenario, t.QueryVariant;
GO

-- =========================================================================
-- INDEX USAGE COMPARISON — per variant per NULL distribution
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' INDEX USAGE — Seeks vs Scans per variant per NULL distribution';
PRINT '=========================================================================';

SELECT
    NullDistribution,
    QueryVariant,
    COUNT(*) AS Tests,
    SUM(CASE WHEN SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) AS Seeks,
    SUM(CASE WHEN SeekOrScan LIKE '%Scan%' THEN 1 ELSE 0 END) AS Scans,
    SUM(CASE WHEN SeekOrScan NOT LIKE '%Seek%' AND SeekOrScan NOT LIKE '%Scan%' THEN 1 ELSE 0 END) AS Other
FROM dbo.TestResults
GROUP BY NullDistribution, QueryVariant
ORDER BY NullDistribution, QueryVariant;
GO

-- =========================================================================
-- CROSS-DISTRIBUTION COMPARISON — same variant across NULL ratios
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' CROSS-DISTRIBUTION — Does NULL ratio affect plan choice?';
PRINT '=========================================================================';

SELECT
    QueryVariant,
    Scenario,
    MAX(CASE WHEN NullDistribution = 'Null10' THEN SeekOrScan END) AS [10%_NULL],
    MAX(CASE WHEN NullDistribution = 'Null30' THEN SeekOrScan END) AS [30%_NULL],
    MAX(CASE WHEN NullDistribution = 'Null60' THEN SeekOrScan END) AS [60%_NULL],
    MAX(CASE WHEN NullDistribution = 'Null10' THEN RowsReturned END) AS [10%_Rows],
    MAX(CASE WHEN NullDistribution = 'Null30' THEN RowsReturned END) AS [30%_Rows],
    MAX(CASE WHEN NullDistribution = 'Null60' THEN RowsReturned END) AS [60%_Rows]
FROM dbo.TestResults
GROUP BY QueryVariant, Scenario
ORDER BY QueryVariant, Scenario;
GO

-- =========================================================================
-- DECISION MATRIX — Go / No-Go per variant
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' DECISION MATRIX';
PRINT '=========================================================================';

SELECT
    QueryVariant,
    -- Correctness: does it ever miss rows vs baseline?
    CASE
        WHEN EXISTS (
            SELECT 1 FROM dbo.TestResults t2
            JOIN dbo.TestResults bl
                ON  bl.NullDistribution = t2.NullDistribution
                AND bl.Scenario = t2.Scenario
                AND bl.QueryVariant = 'Q1-Baseline'
            WHERE t2.QueryVariant = tr.QueryVariant
              AND t2.RowsReturned < bl.RowsReturned
        ) THEN 'FAILS'
        ELSE 'PASS'
    END AS Correctness,
    -- Index behavior across all distributions
    CASE
        WHEN MIN(CASE WHEN SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) = 1 THEN 'All Seeks'
        WHEN MAX(CASE WHEN SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) = 1 THEN 'Mixed'
        ELSE 'All Scans'
    END AS IndexBehavior,
    -- Does behavior degrade as NULL% increases?
    CASE
        WHEN MAX(CASE WHEN NullDistribution = 'Null10' AND SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) = 1
         AND MAX(CASE WHEN NullDistribution = 'Null60' AND SeekOrScan LIKE '%Scan%' THEN 1 ELSE 0 END) = 1
        THEN 'YES - degrades at high NULL%'
        ELSE 'No degradation observed'
    END AS DegradationRisk,
    COUNT(*) AS TotalTests
FROM dbo.TestResults tr
GROUP BY QueryVariant
ORDER BY QueryVariant;
GO

PRINT '';
PRINT '=========================================================================';
PRINT ' RECOMMENDED NEXT STEPS';
PRINT '=========================================================================';
PRINT '1. Review the CROSS-DISTRIBUTION table to see if NULL ratio flips plans';
PRINT '2. Review the DECISION MATRIX for correctness + index behavior summary';
PRINT '3. Click ExecutionPlanXml cells in SSMS to inspect graphical plans:';
PRINT '   SELECT NullDistribution, QueryVariant, Scenario, ExecutionPlanXml';
PRINT '   FROM dbo.TestResults ORDER BY 1, 2, 3;';
PRINT '';
PRINT '=== ANALYSIS COMPLETE ===';
GO
