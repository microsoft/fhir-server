-- =====================================================================================
-- ReferenceResourceTypeId WHERE Clause Performance Test — Focused
-- =====================================================================================
-- Purpose:  Determine whether adding ReferenceResourceTypeId (with IS NULL handling)
--           to generated SQL WHERE clauses improves index utilization without harming
--           correctness or performance at scale (20M rows per table).
--
-- Context:  PR #5285 modifies UntypedReferenceRewriter to add ReferenceResourceTypeId
--           filters. The secondary index on ReferenceSearchParam has column order:
--             (ReferenceResourceId, ReferenceResourceTypeId, SearchParamId, BaseUri,
--              ResourceSurrogateId, ResourceTypeId)
--           Skipping ReferenceResourceTypeId (column 2) limits seek effectiveness.
--           However, ReferenceResourceTypeId is nullable — untyped string references
--           store NULL. We must test whether including IS NULL degrades plans.
--
-- Approach: This is a focused test comparing ONLY:
--             Q1: Baseline (no ReferenceResourceTypeId in WHERE)
--             Q2: All target types IN (...) OR IS NULL (the PR #5285 approach)
--           Tested across THREE NULL distributions (10%, 30%, 60%) at 20M rows/table
--           to validate results at production-like scale.
--
-- Prior:    Run 2 (2M rows, 11 variants) confirmed Q10 (all-types + NULL) passes
--           correctness and improves index seek rate 6x over baseline. This Run 3
--           replicates at 10x scale with a focused comparison.
--
-- Usage:    1. Run Part 1 to create the test database and load data (3 tables)
--           2. Run Part 2 to execute query variants and capture results
--           3. Run Part 3 to review analysis
--
-- Requirements: SQL Server 2019+ (or Azure SQL). sysadmin or dbcreator for Part 1.
--               Part 1 generates ~60M rows total — allow 15-30 minutes.
-- =====================================================================================

-- =====================================================================================
-- PART 1: DATABASE SETUP AND DATA GENERATION
-- =====================================================================================
-- This section is idempotent: it skips database creation and data generation
-- if the tables already exist and contain sufficient data (>1M rows).
-- To force regeneration, drop the tables first:
--   DROP TABLE IF EXISTS dbo.RefSearch_Null10, dbo.RefSearch_Null30, dbo.RefSearch_Null60;
-- =====================================================================================

-- Create database only if it doesn't exist
IF DB_ID('FhirRefTypeIdTest') IS NULL
BEGIN
    CREATE DATABASE FhirRefTypeIdTest;
    PRINT 'Created database FhirRefTypeIdTest';
END
ELSE
    PRINT 'Database FhirRefTypeIdTest already exists — reusing';
GO

USE FhirRefTypeIdTest;
GO

-- =====================================================================================
-- Helper: Create a ReferenceSearchParam table with schema and indexes
-- Skips creation if table already exists.
-- =====================================================================================
IF OBJECT_ID('dbo.CreateTestTable') IS NOT NULL DROP PROCEDURE dbo.CreateTestTable;
GO

CREATE PROCEDURE dbo.CreateTestTable @TableName SYSNAME
AS
BEGIN
    IF OBJECT_ID('dbo.' + @TableName) IS NOT NULL
    BEGIN
        PRINT 'Table already exists: ' + @TableName + ' — skipping creation';
        RETURN;
    END

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
-- Helper: Populate a table with test data at a given NULL percentage.
-- Skips population if the table already has >= 1M rows (allows reuse of existing data).
-- Default is 20M rows per table (10x increase from Run 2).
-- =====================================================================================
IF OBJECT_ID('dbo.PopulateTestTable') IS NOT NULL DROP PROCEDURE dbo.PopulateTestTable;
GO

CREATE PROCEDURE dbo.PopulateTestTable
    @TableName      SYSNAME,
    @NullPercent    INT,            -- 0-100: percentage of rows with NULL ReferenceResourceTypeId
    @TotalRows      INT = 20000000
AS
BEGIN
    SET NOCOUNT ON;

    -- Skip if table already has sufficient data
    DECLARE @ExistingRows BIGINT;
    DECLARE @CheckSql NVARCHAR(MAX) = N'SELECT @cnt = COUNT_BIG(*) FROM (SELECT TOP 1000001 1 AS x FROM dbo.' + QUOTENAME(@TableName) + N') q';
    EXEC sp_executesql @CheckSql, N'@cnt BIGINT OUTPUT', @cnt = @ExistingRows OUTPUT;

    IF @ExistingRows >= 1000000
    BEGIN
        PRINT @TableName + ' already has ' + CAST(@ExistingRows AS VARCHAR) + '+ rows — skipping population';
        -- Still report distribution
        DECLARE @DistSql NVARCHAR(MAX) = N'
        SELECT ''' + @TableName + N''' AS TableName,
            COUNT_BIG(*) AS TotalRows,
            SUM(CAST(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END AS BIGINT)) AS NullRows,
            CAST(100.0 * SUM(CAST(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END AS BIGINT)) / COUNT_BIG(*) AS DECIMAL(5,1)) AS [NullPct],
            SUM(CAST(CASE WHEN ReferenceResourceTypeId = 103 THEN 1 ELSE 0 END AS BIGINT)) AS PatientRows,
            SUM(CAST(CASE WHEN ReferenceResourceTypeId = 104 THEN 1 ELSE 0 END AS BIGINT)) AS PractitionerRows,
            COUNT(DISTINCT ReferenceResourceId) AS DistinctRefIds,
            COUNT(DISTINCT SearchParamId) AS DistinctSearchParams
        FROM dbo.' + QUOTENAME(@TableName) + N';';
        EXEC sp_executesql @DistSql;
        RETURN;
    END

    DECLARE @BatchSize      INT = 50000;
    DECLARE @CurrentBatch   INT = 0;
    DECLARE @SurrogateBase  BIGINT = 100000000000;

    -- =====================================================================================
    -- Realistic data model: each SearchParamId maps to specific valid target types
    -- based on FHIR R4 spec. ReferenceResourceTypeId is chosen ONLY from targets valid
    -- for that search parameter, plus NULL for untyped string references.
    -- =====================================================================================

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

    IF OBJECT_ID('tempdb..#TargetTypes') IS NOT NULL DROP TABLE #TargetTypes;
    CREATE TABLE #TargetTypes (
        SearchParamId   SMALLINT,
        RefTypeId       SMALLINT NULL,
        LowPct          INT,
        HighPct         INT
    );

    -- SearchParamId 414: Patient(103) 80%, Group(107) 8%, Device(106) 7%, Location(108) 5%
    INSERT INTO #TargetTypes VALUES (414, 103,  0, 79), (414, 107, 80, 87), (414, 106, 88, 94), (414, 108, 95, 99);
    -- SearchParamId 217: Patient(103) 90%, Group(107) 10%
    INSERT INTO #TargetTypes VALUES (217, 103,  0, 89), (217, 107, 90, 99);
    -- SearchParamId 300: Practitioner(104) 60%, Organization(105) 25%, Patient(103) 15%
    INSERT INTO #TargetTypes VALUES (300, 104,  0, 59), (300, 105, 60, 84), (300, 103, 85, 99);
    -- SearchParamId 350: Practitioner(104) 80%, RelatedPerson(109) 20%
    INSERT INTO #TargetTypes VALUES (350, 104,  0, 79), (350, 109, 80, 99);
    -- SearchParamId 500: Practitioner(104) 65%, Organization(105) 35%
    INSERT INTO #TargetTypes VALUES (500, 104,  0, 64), (500, 105, 65, 99);
    -- SearchParamId 550: Practitioner(104) 55%, Organization(105) 20%, Device(106) 10%, Patient(103) 10%, RelatedPerson(109) 5%
    INSERT INTO #TargetTypes VALUES (550, 104,  0, 54), (550, 105, 55, 74), (550, 106, 75, 84), (550, 103, 85, 94), (550, 109, 95, 99);
    -- SearchParamId 450: Organization(105) 100%
    INSERT INTO #TargetTypes VALUES (450, 105,  0, 99);

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
                ABS(CHECKSUM(NEWID())) % 100 AS NullRand,
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
                WHEN rd.NullRand >= ' + CAST(100 - @NullPercent AS NVARCHAR) + N' THEN NULL
                ELSE tt.RefTypeId
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
        ) rt
        CROSS APPLY (
            SELECT TOP 1 RefTypeId
            FROM #TargetTypes
            WHERE SearchParamId = rt.SearchParamId
              AND rd.TypeRand BETWEEN LowPct AND HighPct
        ) tt;';

        EXEC sp_executesql @Sql;

        SET @CurrentBatch = @CurrentBatch + 1;
        IF @CurrentBatch % 20 = 0
            PRINT '  Inserted ' + CAST(@CurrentBatch * @BatchSize AS VARCHAR) + ' rows...';
    END

    -- Seed specific scenario IDs
    DECLARE @SeedBase BIGINT = @SurrogateBase + @TotalRows + 1000;

    SET @Sql = N'
    -- S2: Rare ID
    INSERT INTO dbo.' + QUOTENAME(@TableName) + N' VALUES
        (40, ' + CAST(@SeedBase + 1 AS NVARCHAR) + N', 414, NULL, 103, ''rare-singleton-id'', NULL),
        (57, ' + CAST(@SeedBase + 2 AS NVARCHAR) + N', 414, NULL, 103, ''rare-singleton-id'', NULL);
    -- S3: Typed+NULL overlap
    INSERT INTO dbo.' + QUOTENAME(@TableName) + N' VALUES
        (40, ' + CAST(@SeedBase + 11 AS NVARCHAR) + N', 414, NULL, 103, ''overlap-typed-null-id'', NULL),
        (40, ' + CAST(@SeedBase + 12 AS NVARCHAR) + N', 414, NULL, 107, ''overlap-typed-null-id'', NULL),
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
        COUNT_BIG(*) AS TotalRows,
        SUM(CAST(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END AS BIGINT)) AS NullRows,
        CAST(100.0 * SUM(CAST(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END AS BIGINT)) / COUNT_BIG(*) AS DECIMAL(5,1)) AS [NullPct],
        SUM(CAST(CASE WHEN ReferenceResourceTypeId = 103 THEN 1 ELSE 0 END AS BIGINT)) AS PatientRows,
        SUM(CAST(CASE WHEN ReferenceResourceTypeId = 104 THEN 1 ELSE 0 END AS BIGINT)) AS PractitionerRows,
        COUNT(DISTINCT ReferenceResourceId) AS DistinctRefIds,
        COUNT(DISTINCT SearchParamId) AS DistinctSearchParams
    FROM dbo.' + QUOTENAME(@TableName) + N';';
    EXEC sp_executesql @Sql;

    PRINT '  Done: ' + CONVERT(VARCHAR, GETDATE(), 121);
END
GO

-- Populate all three tables (20M rows each, ~60M total)
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
-- Focused comparison: Q1 (Baseline) vs Q2 (All Types + NULL)
-- 2 variants × 4 scenarios × 3 distributions = 24 test executions
-- =====================================================================================

USE FhirRefTypeIdTest;
GO

-- Persistent results table (survives across batches)
IF OBJECT_ID('dbo.TestResults') IS NOT NULL DROP TABLE dbo.TestResults;
CREATE TABLE dbo.TestResults (
    TestId              INT IDENTITY(1,1) PRIMARY KEY,
    NullDistribution    VARCHAR(20),    -- Null10, Null30, Null60
    QueryVariant        VARCHAR(50),    -- Q1-Baseline, Q2-AllTypesNULL
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
        + N'@ReferenceResourceId VARCHAR(64), '
        + N'@Type1 SMALLINT, @Type2 SMALLINT, @Type3 SMALLINT, @Type4 SMALLINT, @cnt INT OUTPUT';

    DECLARE @Cnt INT;
    EXEC sp_executesql @CountSql, @Params,
        @SearchParamId = 414, @ResourceTypeId = 40,
        @ReferenceResourceId = @RefId,
        @Type1 = 103, @Type2 = 107,       -- Patient + Group
        @Type3 = 106, @Type4 = 108,       -- Device + Location
        @cnt = @Cnt OUTPUT;

    -- Capture execution plan from cache
    DECLARE @PlanXml XML;
    SELECT TOP 1 @PlanXml = qp.query_plan
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    -- Extract index operation info from plan XML
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
-- Run Q1 (Baseline) and Q2 (All Types + NULL) across 4 scenarios per table
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
        -- Q1: Baseline — no ReferenceResourceTypeId filter (current production behavior)
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q1-Baseline', @Scen,
            @Desc, @Q, @RefId;

        -- Q2: All 4 target types + IS NULL (the PR #5285 approach with NULL handling)
        SET @Q = N'SELECT ResourceTypeId, ResourceSurrogateId FROM dbo.' + QUOTENAME(@TableName)
            + N' WHERE (ReferenceResourceTypeId IN (@Type1, @Type2, @Type3, @Type4)'
            + N'        OR ReferenceResourceTypeId IS NULL)'
            + N'   AND SearchParamId = @SearchParamId'
            + N'   AND ReferenceResourceId = @ReferenceResourceId'
            + N'   AND ResourceTypeId = @ResourceTypeId';
        EXEC dbo.RunAndCapture @NullDistribution, 'Q2-AllTypesNULL', @Scen,
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
PRINT '=== Running tests on 10% NULL table (20M rows) ===';
EXEC dbo.RunAllTests @TableName = 'RefSearch_Null10', @NullDistribution = 'Null10';
GO
PRINT '=== Running tests on 30% NULL table (20M rows) ===';
EXEC dbo.RunAllTests @TableName = 'RefSearch_Null30', @NullDistribution = 'Null30';
GO
PRINT '=== Running tests on 60% NULL table (20M rows) ===';
EXEC dbo.RunAllTests @TableName = 'RefSearch_Null60', @NullDistribution = 'Null60';
GO


-- =====================================================================================
-- PART 3: RESULTS ANALYSIS
-- =====================================================================================

PRINT '';
PRINT '=========================================================================';
PRINT ' RESULTS SUMMARY — Baseline vs AllTypesNULL at 20M rows';
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
-- CORRECTNESS CHECK: Q2 row counts must match Q1 baseline
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' CORRECTNESS CHECK — Q2 vs Q1 row counts';
PRINT '=========================================================================';

SELECT
    t.NullDistribution,
    t.Scenario,
    t.QueryVariant,
    t.RowsReturned AS Q2_Rows,
    bl.RowsReturned AS Q1_Baseline_Rows,
    t.RowsReturned - bl.RowsReturned AS RowDiff,
    CASE
        WHEN t.RowsReturned = bl.RowsReturned THEN 'PASS'
        WHEN t.RowsReturned < bl.RowsReturned THEN '!! MISSING ROWS'
        WHEN t.RowsReturned > bl.RowsReturned THEN '!! EXTRA ROWS'
    END AS Status
FROM dbo.TestResults t
JOIN dbo.TestResults bl
    ON  bl.NullDistribution = t.NullDistribution
    AND bl.Scenario         = t.Scenario
    AND bl.QueryVariant     = 'Q1-Baseline'
WHERE t.QueryVariant = 'Q2-AllTypesNULL'
ORDER BY t.NullDistribution, t.Scenario;
GO

-- =========================================================================
-- INDEX BEHAVIOR: Side-by-side seek/scan comparison
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' INDEX BEHAVIOR — Side-by-side: Baseline vs AllTypesNULL';
PRINT '=========================================================================';

SELECT
    bl.NullDistribution,
    bl.Scenario,
    bl.SeekOrScan       AS Q1_Baseline_Plan,
    bl.IndexUsed        AS Q1_Index,
    t.SeekOrScan        AS Q2_AllTypesNULL_Plan,
    t.IndexUsed         AS Q2_Index,
    CASE
        WHEN bl.SeekOrScan = 'N/A'         AND t.SeekOrScan LIKE '%Seek%' THEN 'IMPROVED'
        WHEN bl.SeekOrScan LIKE '%Seek%'    AND t.SeekOrScan = 'N/A'      THEN 'REGRESSED'
        WHEN bl.SeekOrScan LIKE '%Seek%'    AND t.SeekOrScan LIKE '%Seek%' THEN 'SAME (both seek)'
        WHEN bl.SeekOrScan LIKE '%Scan%'    AND t.SeekOrScan LIKE '%Seek%' THEN 'IMPROVED'
        WHEN bl.SeekOrScan LIKE '%Seek%'    AND t.SeekOrScan LIKE '%Scan%' THEN 'REGRESSED'
        WHEN bl.SeekOrScan = t.SeekOrScan                                  THEN 'SAME'
        ELSE 'CHANGED'
    END AS Delta
FROM dbo.TestResults bl
JOIN dbo.TestResults t
    ON  t.NullDistribution = bl.NullDistribution
    AND t.Scenario         = bl.Scenario
    AND t.QueryVariant     = 'Q2-AllTypesNULL'
WHERE bl.QueryVariant = 'Q1-Baseline'
ORDER BY bl.NullDistribution, bl.Scenario;
GO

-- =========================================================================
-- CROSS-DISTRIBUTION — Plan stability across NULL ratios
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' CROSS-DISTRIBUTION — Plan stability across NULL ratios';
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
-- SUMMARY VERDICT
-- =========================================================================
PRINT '';
PRINT '=========================================================================';
PRINT ' SUMMARY VERDICT';
PRINT '=========================================================================';

SELECT
    QueryVariant,
    -- Correctness
    CASE
        WHEN QueryVariant = 'Q1-Baseline' THEN 'BASELINE'
        WHEN EXISTS (
            SELECT 1 FROM dbo.TestResults t2
            JOIN dbo.TestResults bl
                ON  bl.NullDistribution = t2.NullDistribution
                AND bl.Scenario = t2.Scenario
                AND bl.QueryVariant = 'Q1-Baseline'
            WHERE t2.QueryVariant = tr.QueryVariant
              AND t2.RowsReturned <> bl.RowsReturned
        ) THEN 'FAILS'
        ELSE 'PASS'
    END AS Correctness,
    -- Index seeks
    SUM(CASE WHEN SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) AS SeekCount,
    COUNT(*) AS TotalTests,
    CAST(100.0 * SUM(CASE WHEN SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,1)) AS [SeekPct],
    -- Degradation risk
    CASE
        WHEN MAX(CASE WHEN NullDistribution = 'Null10' AND SeekOrScan LIKE '%Seek%' THEN 1 ELSE 0 END) = 1
         AND MAX(CASE WHEN NullDistribution = 'Null60' AND SeekOrScan LIKE '%Scan%' THEN 1 ELSE 0 END) = 1
        THEN 'YES - degrades at high NULL%'
        ELSE 'No degradation observed'
    END AS DegradationRisk
FROM dbo.TestResults tr
GROUP BY QueryVariant
ORDER BY QueryVariant;
GO

PRINT '';
PRINT '=========================================================================';
PRINT ' TO INSPECT EXECUTION PLANS:';
PRINT '=========================================================================';
PRINT '  SELECT NullDistribution, QueryVariant, Scenario, ExecutionPlanXml';
PRINT '  FROM dbo.TestResults ORDER BY 1, 2, 3;';
PRINT '';
PRINT '=== ANALYSIS COMPLETE ===';
GO
