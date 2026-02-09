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
-- Usage:    1. Run Part 1 to create the test database and load data
--           2. Run Part 2 to execute all query variants and capture results
--           3. Review the #TestResults table for analysis
--
-- Requirements: SQL Server 2019+ (or Azure SQL). sysadmin or dbcreator for Part 1.
-- =====================================================================================

-- =====================================================================================
-- PART 1: DATABASE SETUP AND DATA GENERATION
-- =====================================================================================

USE master;
GO

-- Create the test database (adjust path if needed)
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

-- We skip partitioning for simplicity — it maps all partitions to PRIMARY anyway.
-- The query optimizer behavior for index seek vs scan is unaffected.

CREATE TABLE dbo.ReferenceSearchParam
(
    ResourceTypeId                      smallint                NOT NULL,
    ResourceSurrogateId                 bigint                  NOT NULL,
    SearchParamId                       smallint                NOT NULL,
    BaseUri                             varchar(128)            COLLATE Latin1_General_100_CS_AS NULL,
    ReferenceResourceTypeId             smallint                NULL,
    ReferenceResourceId                 varchar(64)             COLLATE Latin1_General_100_CS_AS NOT NULL,
    ReferenceResourceVersion            int                     NULL
);
GO

ALTER TABLE dbo.ReferenceSearchParam SET ( LOCK_ESCALATION = AUTO );
GO

-- Clustered index (matches production)
CREATE CLUSTERED INDEX IXC_ReferenceSearchParam
ON dbo.ReferenceSearchParam
(
    ResourceTypeId,
    ResourceSurrogateId,
    SearchParamId
)
WITH (DATA_COMPRESSION = PAGE);
GO

-- Secondary index — this is the index we are trying to leverage
CREATE UNIQUE INDEX IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId
ON dbo.ReferenceSearchParam
(
    ReferenceResourceId,
    ReferenceResourceTypeId,
    SearchParamId,
    BaseUri,
    ResourceSurrogateId,
    ResourceTypeId
)
WITH (DATA_COMPRESSION = PAGE);
GO

-- =====================================================================================
-- DATA GENERATION
-- =====================================================================================
-- Target: ~2M rows with realistic distribution
--
-- ResourceTypeId values (source resource types):
--   40 = DiagnosticReport, 57 = Observation, 20 = Condition,
--   25 = Encounter, 15 = Claim, 50 = MedicationRequest
--
-- ReferenceResourceTypeId values (target reference types):
--   103 = Patient (40%), 104 = Practitioner (15%), 105 = Organization (10%),
--   106 = Device (5%), 107 = Group (5%), NULL (10%), Various others (15%)
--
-- SearchParamId values:
--   414 = DiagnosticReport-subject, 217 = clinical-patient,
--   300 = Observation-performer, 350 = Encounter-participant,
--   400 = general-practitioner, 450 = managing-organization,
--   500 = Claim-provider, 550 = MedicationRequest-requester
-- =====================================================================================

SET NOCOUNT ON;

DECLARE @TotalRows        INT = 2000000;
DECLARE @BatchSize        INT = 50000;
DECLARE @CurrentBatch     INT = 0;
DECLARE @SurrogateIdBase  BIGINT = 100000000000;

-- Source resource types and their search params
DECLARE @ResourceTypes TABLE (ResourceTypeId SMALLINT, SearchParamId SMALLINT, Weight INT);
INSERT INTO @ResourceTypes VALUES
    (40,  414, 25),   -- DiagnosticReport / subject
    (40,  217, 15),   -- DiagnosticReport / clinical-patient
    (57,  414, 20),   -- Observation / subject
    (57,  300, 10),   -- Observation / performer
    (20,  217,  8),   -- Condition / clinical-patient
    (25,  350,  7),   -- Encounter / participant
    (15,  500,  5),   -- Claim / provider
    (50,  550,  5),   -- MedicationRequest / requester
    (25,  450,  5);   -- Encounter / managing-organization

-- Weighted cumulative distribution for resource type selection
DECLARE @ResourceTypesCumulative TABLE (
    ResourceTypeId SMALLINT, SearchParamId SMALLINT,
    LowBound INT, HighBound INT
);

DECLARE @RunningTotal INT = 0;
INSERT INTO @ResourceTypesCumulative
SELECT ResourceTypeId, SearchParamId,
       @RunningTotal AS LowBound,
       @RunningTotal + Weight - 1 AS HighBound
FROM (
    SELECT ResourceTypeId, SearchParamId, Weight,
           SUM(Weight) OVER (ORDER BY ResourceTypeId, SearchParamId
                             ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) - Weight AS Offset
    FROM @ResourceTypes
) x
ORDER BY ResourceTypeId, SearchParamId;

-- Recalculate properly with running total
DELETE FROM @ResourceTypesCumulative;

DECLARE @rtId SMALLINT, @spId SMALLINT, @w INT;
DECLARE rt_cursor CURSOR FAST_FORWARD FOR
    SELECT ResourceTypeId, SearchParamId, Weight FROM @ResourceTypes ORDER BY ResourceTypeId, SearchParamId;
OPEN rt_cursor;
FETCH NEXT FROM rt_cursor INTO @rtId, @spId, @w;
WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO @ResourceTypesCumulative VALUES (@rtId, @spId, @RunningTotal, @RunningTotal + @w - 1);
    SET @RunningTotal = @RunningTotal + @w;
    FETCH NEXT FROM rt_cursor INTO @rtId, @spId, @w;
END
CLOSE rt_cursor;
DEALLOCATE rt_cursor;

PRINT 'Generating ' + CAST(@TotalRows AS VARCHAR) + ' rows...';
PRINT 'Start time: ' + CONVERT(VARCHAR, GETDATE(), 121);

WHILE @CurrentBatch * @BatchSize < @TotalRows
BEGIN
    ;WITH Numbers AS (
        SELECT TOP (@BatchSize)
            ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
        FROM sys.all_objects a CROSS JOIN sys.all_objects b
    ),
    RandomData AS (
        SELECT
            N,
            ABS(CHECKSUM(NEWID())) % @RunningTotal AS ResourceRand,
            ABS(CHECKSUM(NEWID())) % 100 AS TypeRand,
            ABS(CHECKSUM(NEWID())) % 20000 AS RefIdRand,  -- 20K distinct reference IDs
            @SurrogateIdBase + (@CurrentBatch * @BatchSize) + N AS SurrogateId
        FROM Numbers
    )
    INSERT INTO dbo.ReferenceSearchParam (
        ResourceTypeId, ResourceSurrogateId, SearchParamId,
        BaseUri, ReferenceResourceTypeId, ReferenceResourceId, ReferenceResourceVersion
    )
    SELECT
        rt.ResourceTypeId,
        rd.SurrogateId,
        rt.SearchParamId,
        NULL,  -- BaseUri
        CASE
            WHEN rd.TypeRand < 40  THEN 103  -- Patient (40%)
            WHEN rd.TypeRand < 55  THEN 104  -- Practitioner (15%)
            WHEN rd.TypeRand < 65  THEN 105  -- Organization (10%)
            WHEN rd.TypeRand < 70  THEN 106  -- Device (5%)
            WHEN rd.TypeRand < 75  THEN 107  -- Group (5%)
            WHEN rd.TypeRand < 85  THEN NULL -- Untyped / NULL (10%)
            WHEN rd.TypeRand < 90  THEN 108  -- Location (5%)
            WHEN rd.TypeRand < 95  THEN 109  -- RelatedPerson (5%)
            ELSE                       110   -- Medication (5%)
        END,
        -- ReferenceResourceId: mix of common and rare IDs
        CASE
            WHEN rd.RefIdRand < 100 THEN 'common-patient-' + RIGHT('0000' + CAST(rd.RefIdRand % 10 AS VARCHAR), 4)
            WHEN rd.RefIdRand < 500 THEN 'patient-' + RIGHT('0000' + CAST(rd.RefIdRand AS VARCHAR), 5)
            ELSE 'ref-' + CAST(rd.RefIdRand AS VARCHAR)
        END,
        NULL  -- ReferenceResourceVersion
    FROM RandomData rd
    CROSS APPLY (
        SELECT TOP 1 ResourceTypeId, SearchParamId
        FROM @ResourceTypesCumulative
        WHERE rd.ResourceRand BETWEEN LowBound AND HighBound
    ) rt;

    SET @CurrentBatch = @CurrentBatch + 1;

    IF @CurrentBatch % 10 = 0
        PRINT '  Inserted ' + CAST(@CurrentBatch * @BatchSize AS VARCHAR) + ' rows...';
END

PRINT 'Data generation complete. Total rows: ' + CAST((SELECT COUNT(*) FROM dbo.ReferenceSearchParam) AS VARCHAR);
PRINT 'End time: ' + CONVERT(VARCHAR, GETDATE(), 121);
GO

-- =====================================================================================
-- Ensure specific test scenario IDs exist
-- =====================================================================================
-- S1: "common-patient-0001" should have many rows (common ID)
-- S2: "rare-singleton-id" should have very few rows (rare ID)
-- S3: "overlap-typed-null-id" should have BOTH typed and NULL rows
-- S4: "null-only-id" should have ONLY NULL ReferenceResourceTypeId

-- S2: Insert a few rows with a rare ID
INSERT INTO dbo.ReferenceSearchParam VALUES
    (40, 999999999901, 414, NULL, 103, 'rare-singleton-id', NULL),
    (57, 999999999902, 414, NULL, 103, 'rare-singleton-id', NULL);

-- S3: Insert rows with both typed and NULL for same ID
INSERT INTO dbo.ReferenceSearchParam VALUES
    (40, 999999999911, 414, NULL, 103, 'overlap-typed-null-id', NULL),
    (40, 999999999912, 414, NULL, 104, 'overlap-typed-null-id', NULL),
    (57, 999999999913, 414, NULL, NULL, 'overlap-typed-null-id', NULL),
    (57, 999999999914, 217, NULL, NULL, 'overlap-typed-null-id', NULL),
    (20, 999999999915, 217, NULL, 103, 'overlap-typed-null-id', NULL);

-- S4: Insert rows with ONLY NULL ReferenceResourceTypeId
INSERT INTO dbo.ReferenceSearchParam VALUES
    (40, 999999999921, 414, NULL, NULL, 'null-only-id', NULL),
    (57, 999999999922, 414, NULL, NULL, 'null-only-id', NULL),
    (20, 999999999923, 217, NULL, NULL, 'null-only-id', NULL);
GO

-- =====================================================================================
-- Rebuild indexes and update statistics for accurate query plans
-- =====================================================================================
ALTER INDEX IXC_ReferenceSearchParam ON dbo.ReferenceSearchParam REBUILD WITH (DATA_COMPRESSION = PAGE);
ALTER INDEX IXU_ReferenceResourceId_ReferenceResourceTypeId_SearchParamId_BaseUri_ResourceSurrogateId_ResourceTypeId
    ON dbo.ReferenceSearchParam REBUILD WITH (DATA_COMPRESSION = PAGE);

UPDATE STATISTICS dbo.ReferenceSearchParam WITH FULLSCAN;
GO

-- Verify data distribution
SELECT
    'Distribution' AS Label,
    COUNT(*) AS TotalRows,
    SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END) AS NullTypeRows,
    SUM(CASE WHEN ReferenceResourceTypeId = 103 THEN 1 ELSE 0 END) AS PatientRows,
    SUM(CASE WHEN ReferenceResourceTypeId = 104 THEN 1 ELSE 0 END) AS PractitionerRows,
    SUM(CASE WHEN ReferenceResourceTypeId = 105 THEN 1 ELSE 0 END) AS OrganizationRows,
    COUNT(DISTINCT ReferenceResourceId) AS DistinctRefIds,
    COUNT(DISTINCT SearchParamId) AS DistinctSearchParams,
    COUNT(DISTINCT ResourceTypeId) AS DistinctResourceTypes
FROM dbo.ReferenceSearchParam;

SELECT
    'Scenario IDs' AS Label,
    ReferenceResourceId,
    COUNT(*) AS RowCount,
    SUM(CASE WHEN ReferenceResourceTypeId IS NULL THEN 1 ELSE 0 END) AS NullCount,
    SUM(CASE WHEN ReferenceResourceTypeId IS NOT NULL THEN 1 ELSE 0 END) AS TypedCount
FROM dbo.ReferenceSearchParam
WHERE ReferenceResourceId IN ('common-patient-0001', 'rare-singleton-id', 'overlap-typed-null-id', 'null-only-id')
GROUP BY ReferenceResourceId
ORDER BY ReferenceResourceId;
GO

PRINT '=== PART 1 COMPLETE: Database ready for testing ===';
GO


-- =====================================================================================
-- PART 2: QUERY EXECUTION AND METRICS CAPTURE
-- =====================================================================================

USE FhirRefTypeIdTest;
GO

-- Results table to hold all metrics
IF OBJECT_ID('tempdb..#TestResults') IS NOT NULL DROP TABLE #TestResults;
CREATE TABLE #TestResults (
    TestId              INT IDENTITY(1,1),
    QueryVariant        VARCHAR(50),    -- Q1-Q6
    Scenario            VARCHAR(50),    -- S1-S4
    Description         VARCHAR(200),
    RowsReturned        INT,
    LogicalReads        INT,
    CpuTimeMs           INT,
    ElapsedTimeMs       INT,
    IndexUsed           VARCHAR(200),
    SeekOrScan          VARCHAR(20),
    EstimatedRows       FLOAT,
    ActualRows          INT,
    ExecutionPlanXml    XML
);
GO

-- =====================================================================================
-- Helper procedure: Runs a query, captures IO/time/plan, stores in #TestResults
-- =====================================================================================
IF OBJECT_ID('dbo.RunQueryTest') IS NOT NULL DROP PROCEDURE dbo.RunQueryTest;
GO

CREATE PROCEDURE dbo.RunQueryTest
    @QueryVariant   VARCHAR(50),
    @Scenario       VARCHAR(50),
    @Description    VARCHAR(200),
    @SqlText        NVARCHAR(MAX),
    @Params         NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RowCount       INT;
    DECLARE @PlanXml        XML;
    DECLARE @CpuStart       BIGINT;
    DECLARE @ElapsedStart   DATETIME2(7) = SYSUTCDATETIME();
    DECLARE @CpuEnd         BIGINT;
    DECLARE @IoReads        BIGINT;

    -- Snapshot CPU before
    SELECT @CpuStart = cpu_time FROM sys.dm_exec_sessions WHERE session_id = @@SPID;

    -- Clear buffer cache for this test (forces physical reads on first run; comment out for warm-cache test)
    -- DBCC DROPCLEANBUFFERS;  -- Uncomment for cold-cache testing

    -- Execute the query and capture plan
    DECLARE @PlanHandle VARBINARY(64);

    -- Use SET STATISTICS XML to capture the plan
    -- We wrap the actual query in a sub-batch approach
    DECLARE @WrappedSql NVARCHAR(MAX) = @SqlText;

    -- Execute and count rows
    DECLARE @CountSql NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @SqlText + N') AS q';
    DECLARE @CountParams NVARCHAR(MAX) = @Params + N', @cnt INT OUTPUT';
    EXEC sp_executesql @CountSql, @CountParams,
        @SearchParamId = 414, @ResourceTypeId = 40,
        @ReferenceResourceId = '', -- will be overridden per scenario
        @ReferenceResourceTypeId = 103,
        @Type1 = 103, @Type2 = 104,
        @cnt = @RowCount OUTPUT;

    -- Snapshot CPU after
    SELECT @CpuEnd = cpu_time FROM sys.dm_exec_sessions WHERE session_id = @@SPID;

    -- Get IO reads from dm_exec_query_stats for the most recent query
    SELECT TOP 1 @IoReads = total_logical_reads
    FROM sys.dm_exec_query_stats
    ORDER BY last_execution_time DESC;

    -- Get the execution plan XML
    SELECT TOP 1 @PlanXml = query_plan
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle)
    ORDER BY qs.last_execution_time DESC;

    -- Extract index info from plan
    DECLARE @IndexName VARCHAR(200) = '';
    DECLARE @SeekOrScan VARCHAR(20) = '';

    -- Parse plan XML for index operations
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1
        @IndexName = ISNULL(node.value('(@Index)[1]', 'VARCHAR(200)'), 'N/A'),
        @SeekOrScan = ISNULL(node.value('local-name(.)', 'VARCHAR(20)'), 'N/A')
    FROM @PlanXml.nodes('//IndexScan | //IndexSeek') AS plan_nodes(node);

    -- Extract estimated rows
    DECLARE @EstRows FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @EstRows = node.value('(@EstimateRows)[1]', 'FLOAT')
    FROM @PlanXml.nodes('//RelOp') AS plan_nodes(node)
    WHERE node.value('(@PhysicalOp)[1]', 'VARCHAR(50)') IN ('Index Seek', 'Index Scan', 'Clustered Index Scan', 'Clustered Index Seek');

    INSERT INTO #TestResults (
        QueryVariant, Scenario, Description, RowsReturned,
        LogicalReads, CpuTimeMs, ElapsedTimeMs,
        IndexUsed, SeekOrScan, EstimatedRows, ActualRows, ExecutionPlanXml
    )
    VALUES (
        @QueryVariant, @Scenario, @Description, @RowCount,
        @IoReads, @CpuEnd - @CpuStart, DATEDIFF(MILLISECOND, @ElapsedStart, SYSUTCDATETIME()),
        @IndexName, @SeekOrScan, @EstRows, @RowCount, @PlanXml
    );

    PRINT @QueryVariant + ' / ' + @Scenario + ': ' + CAST(@RowCount AS VARCHAR) + ' rows, '
        + @SeekOrScan + ' on ' + @IndexName;
END
GO

-- =====================================================================================
-- Run all query variants against all scenarios
-- =====================================================================================
-- We parameterize per scenario by changing @ReferenceResourceId

DECLARE @Scenarios TABLE (
    Scenario        VARCHAR(10),
    RefId           VARCHAR(64),
    Description     VARCHAR(100)
);
INSERT INTO @Scenarios VALUES
    ('S1', 'common-patient-0001', 'Common ID with many rows'),
    ('S2', 'rare-singleton-id',   'Rare ID with few rows'),
    ('S3', 'overlap-typed-null-id', 'ID in both typed and NULL rows'),
    ('S4', 'null-only-id',         'ID only in NULL rows');

DECLARE @ScenarioName VARCHAR(10), @RefId VARCHAR(64), @ScenDesc VARCHAR(100);

DECLARE scenario_cursor CURSOR FAST_FORWARD FOR
    SELECT Scenario, RefId, Description FROM @Scenarios;

OPEN scenario_cursor;
FETCH NEXT FROM scenario_cursor INTO @ScenarioName, @RefId, @ScenDesc;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @BaseParams NVARCHAR(MAX) = N'@SearchParamId SMALLINT, @ResourceTypeId SMALLINT, @ReferenceResourceId VARCHAR(64), @ReferenceResourceTypeId SMALLINT, @Type1 SMALLINT, @Type2 SMALLINT';

    -- ========== Q1: Baseline — no ReferenceResourceTypeId filter ==========
    DECLARE @Q1 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q1Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q1 + N') q';
    DECLARE @Q1Cnt INT;
    EXEC sp_executesql @Q1Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q1Cnt OUTPUT;

    -- Capture plan for Q1
    DECLARE @Q1Plan XML;
    SELECT TOP 1 @Q1Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q1Index VARCHAR(200) = '', @Q1SeekScan VARCHAR(20) = '', @Q1Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q1Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q1SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q1Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q1Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q1Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q1-Baseline', @ScenarioName, @ScenDesc + ' / No type filter', @Q1Cnt, @Q1Index, @Q1SeekScan, @Q1Est, @Q1Cnt, @Q1Plan);

    -- ========== Q2: Single type equality ==========
    DECLARE @Q2 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE SearchParamId = @SearchParamId
          AND ReferenceResourceTypeId = @ReferenceResourceTypeId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q2Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q2 + N') q';
    DECLARE @Q2Cnt INT;
    EXEC sp_executesql @Q2Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q2Cnt OUTPUT;

    DECLARE @Q2Plan XML;
    SELECT TOP 1 @Q2Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q2Index VARCHAR(200) = '', @Q2SeekScan VARCHAR(20) = '', @Q2Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q2Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q2SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q2Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q2Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q2Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q2-SingleType', @ScenarioName, @ScenDesc + ' / Single type equality', @Q2Cnt, @Q2Index, @Q2SeekScan, @Q2Est, @Q2Cnt, @Q2Plan);

    -- ========== Q3: Multiple types (OR) ==========
    DECLARE @Q3 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE (ReferenceResourceTypeId = @Type1 OR ReferenceResourceTypeId = @Type2)
          AND SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q3Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q3 + N') q';
    DECLARE @Q3Cnt INT;
    EXEC sp_executesql @Q3Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q3Cnt OUTPUT;

    DECLARE @Q3Plan XML;
    SELECT TOP 1 @Q3Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q3Index VARCHAR(200) = '', @Q3SeekScan VARCHAR(20) = '', @Q3Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q3Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q3SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q3Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q3Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q3Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q3-MultiTypeOR', @ScenarioName, @ScenDesc + ' / Multi-type OR', @Q3Cnt, @Q3Index, @Q3SeekScan, @Q3Est, @Q3Cnt, @Q3Plan);

    -- ========== Q4: Single type + NULL ==========
    DECLARE @Q4 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE (ReferenceResourceTypeId = @ReferenceResourceTypeId OR ReferenceResourceTypeId IS NULL)
          AND SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q4Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q4 + N') q';
    DECLARE @Q4Cnt INT;
    EXEC sp_executesql @Q4Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q4Cnt OUTPUT;

    DECLARE @Q4Plan XML;
    SELECT TOP 1 @Q4Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q4Index VARCHAR(200) = '', @Q4SeekScan VARCHAR(20) = '', @Q4Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q4Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q4SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q4Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q4Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q4Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q4-SingleTypeNULL', @ScenarioName, @ScenDesc + ' / Single type + IS NULL', @Q4Cnt, @Q4Index, @Q4SeekScan, @Q4Est, @Q4Cnt, @Q4Plan);

    -- ========== Q5: Multiple types + NULL ==========
    DECLARE @Q5 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE (ReferenceResourceTypeId = @Type1 OR ReferenceResourceTypeId = @Type2 OR ReferenceResourceTypeId IS NULL)
          AND SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q5Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q5 + N') q';
    DECLARE @Q5Cnt INT;
    EXEC sp_executesql @Q5Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q5Cnt OUTPUT;

    DECLARE @Q5Plan XML;
    SELECT TOP 1 @Q5Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q5Index VARCHAR(200) = '', @Q5SeekScan VARCHAR(20) = '', @Q5Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q5Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q5SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q5Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q5Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q5Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q5-MultiTypeNULL', @ScenarioName, @ScenDesc + ' / Multi-type OR + IS NULL', @Q5Cnt, @Q5Index, @Q5SeekScan, @Q5Est, @Q5Cnt, @Q5Plan);

    -- ========== Q6: IN clause (no NULL) ==========
    DECLARE @Q6 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE ReferenceResourceTypeId IN (@Type1, @Type2)
          AND SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q6Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q6 + N') q';
    DECLARE @Q6Cnt INT;
    EXEC sp_executesql @Q6Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q6Cnt OUTPUT;

    DECLARE @Q6Plan XML;
    SELECT TOP 1 @Q6Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q6Index VARCHAR(200) = '', @Q6SeekScan VARCHAR(20) = '', @Q6Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q6Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q6SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q6Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q6Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q6Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q6-InClause', @ScenarioName, @ScenDesc + ' / IN clause (no NULL)', @Q6Cnt, @Q6Index, @Q6SeekScan, @Q6Est, @Q6Cnt, @Q6Plan);

    -- ========== Q7: UNION ALL approach (type seek UNION ALL NULL seek) ==========
    DECLARE @Q7 NVARCHAR(MAX) = N'
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE ReferenceResourceTypeId = @ReferenceResourceTypeId
          AND SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId
        UNION ALL
        SELECT ResourceTypeId, ResourceSurrogateId
        FROM dbo.ReferenceSearchParam
        WHERE ReferenceResourceTypeId IS NULL
          AND SearchParamId = @SearchParamId
          AND ReferenceResourceId = @ReferenceResourceId
          AND ResourceTypeId = @ResourceTypeId';

    DECLARE @Q7Count NVARCHAR(MAX) = N'SELECT @cnt = COUNT(*) FROM (' + @Q7 + N') q';
    DECLARE @Q7Cnt INT;
    EXEC sp_executesql @Q7Count, @BaseParams + N', @cnt INT OUTPUT',
        @SearchParamId = 414, @ResourceTypeId = 40, @ReferenceResourceId = @RefId,
        @ReferenceResourceTypeId = 103, @Type1 = 103, @Type2 = 104, @cnt = @Q7Cnt OUTPUT;

    DECLARE @Q7Plan XML;
    SELECT TOP 1 @Q7Plan = qp.query_plan
    FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
    ORDER BY qs.last_execution_time DESC;

    DECLARE @Q7Index VARCHAR(200) = '', @Q7SeekScan VARCHAR(20) = '', @Q7Est FLOAT = 0;
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q7Index = ISNULL(n.value('(@Index)[1]','VARCHAR(200)'),'N/A'),
                 @Q7SeekScan = ISNULL(n.value('local-name(.)','VARCHAR(20)'),'N/A')
    FROM @Q7Plan.nodes('//IndexScan | //IndexSeek') AS x(n);
    ;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
    SELECT TOP 1 @Q7Est = n.value('(@EstimateRows)[1]','FLOAT')
    FROM @Q7Plan.nodes('//RelOp') AS x(n)
    WHERE n.value('(@PhysicalOp)[1]','VARCHAR(50)') IN ('Index Seek','Index Scan','Clustered Index Scan','Clustered Index Seek');

    INSERT INTO #TestResults (QueryVariant,Scenario,Description,RowsReturned,IndexUsed,SeekOrScan,EstimatedRows,ActualRows,ExecutionPlanXml)
    VALUES ('Q7-UnionAll', @ScenarioName, @ScenDesc + ' / UNION ALL (type + NULL)', @Q7Cnt, @Q7Index, @Q7SeekScan, @Q7Est, @Q7Cnt, @Q7Plan);

    FETCH NEXT FROM scenario_cursor INTO @ScenarioName, @RefId, @ScenDesc;
END

CLOSE scenario_cursor;
DEALLOCATE scenario_cursor;
GO

-- =====================================================================================
-- PART 3: RESULTS ANALYSIS
-- =====================================================================================

-- Summary comparison across all variants and scenarios
PRINT '';
PRINT '=======================================================================';
PRINT ' RESULTS SUMMARY';
PRINT '=======================================================================';

SELECT
    QueryVariant,
    Scenario,
    RowsReturned,
    IndexUsed,
    SeekOrScan,
    CAST(EstimatedRows AS INT) AS EstRows,
    ActualRows,
    Description
FROM #TestResults
ORDER BY Scenario, QueryVariant;

-- Correctness check: compare row counts between variants within each scenario
PRINT '';
PRINT '=======================================================================';
PRINT ' CORRECTNESS CHECK: Row count differences vs baseline (Q1)';
PRINT '=======================================================================';

SELECT
    t.Scenario,
    t.QueryVariant,
    t.RowsReturned,
    baseline.RowsReturned AS BaselineRows,
    t.RowsReturned - baseline.RowsReturned AS RowDifference,
    CASE
        WHEN t.RowsReturned < baseline.RowsReturned THEN '!! MISSING ROWS - correctness issue'
        WHEN t.RowsReturned > baseline.RowsReturned THEN '+ Extra rows returned'
        ELSE 'OK - matches baseline'
    END AS CorrectnessStatus
FROM #TestResults t
JOIN #TestResults baseline ON baseline.Scenario = t.Scenario AND baseline.QueryVariant = 'Q1-Baseline'
WHERE t.QueryVariant <> 'Q1-Baseline'
ORDER BY t.Scenario, t.QueryVariant;

-- Performance comparison: index usage patterns
PRINT '';
PRINT '=======================================================================';
PRINT ' INDEX USAGE COMPARISON';
PRINT '=======================================================================';

SELECT
    QueryVariant,
    COUNT(*) AS TotalTests,
    SUM(CASE WHEN SeekOrScan = 'IndexSeek' THEN 1 ELSE 0 END) AS SeekCount,
    SUM(CASE WHEN SeekOrScan = 'IndexScan' THEN 1 ELSE 0 END) AS ScanCount,
    SUM(CASE WHEN SeekOrScan NOT IN ('IndexSeek', 'IndexScan') THEN 1 ELSE 0 END) AS OtherCount
FROM #TestResults
GROUP BY QueryVariant
ORDER BY QueryVariant;

-- Decision matrix
PRINT '';
PRINT '=======================================================================';
PRINT ' DECISION MATRIX';
PRINT '=======================================================================';

SELECT
    QueryVariant,
    CASE
        WHEN SUM(CASE WHEN SeekOrScan = 'IndexSeek' THEN 1 ELSE 0 END) = COUNT(*) THEN 'All Seeks'
        WHEN SUM(CASE WHEN SeekOrScan = 'IndexSeek' THEN 1 ELSE 0 END) > 0 THEN 'Mixed Seek/Scan'
        ELSE 'All Scans'
    END AS IndexBehavior,
    MIN(RowsReturned) AS MinRows,
    MAX(RowsReturned) AS MaxRows,
    CASE
        WHEN QueryVariant IN ('Q2-SingleType', 'Q3-MultiTypeOR', 'Q6-InClause')
             AND EXISTS (
                SELECT 1 FROM #TestResults t2
                JOIN #TestResults bl ON bl.Scenario = t2.Scenario AND bl.QueryVariant = 'Q1-Baseline'
                WHERE t2.QueryVariant = #TestResults.QueryVariant
                  AND t2.RowsReturned < bl.RowsReturned
             )
        THEN 'FAILS CORRECTNESS - misses NULL rows'
        ELSE 'Correct or needs review'
    END AS CorrectnessNote
FROM #TestResults
GROUP BY QueryVariant
ORDER BY QueryVariant;

PRINT '';
PRINT '=== PART 3 COMPLETE: Review results above ===';
PRINT 'Note: Full execution plans are stored in #TestResults.ExecutionPlanXml';
PRINT 'Use: SELECT QueryVariant, Scenario, ExecutionPlanXml FROM #TestResults';
PRINT 'to inspect individual plans in SSMS (click XML to view graphical plan).';
GO
