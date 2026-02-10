// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Unit tests for SqlServerSearchService query text normalization and batch splitting methods
    /// used by the Query Store long-running query logging feature.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchServiceQueryStoreTests
    {
        private static readonly MethodInfo StripQueryPreambleLines =
            typeof(SqlServerSearchService).GetMethod(
                "StripQueryPreambleLines",
                BindingFlags.NonPublic | BindingFlags.Static);

        private static string InvokeStripQueryPreambleLines(string queryText)
        {
            return (string)StripQueryPreambleLines.Invoke(null, new object[] { queryText });
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void StripQueryPreambleLines_NullEmptyOrWhitespaceInput_ReturnsEmpty(string input)
        {
            // Arrange & Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.Equal(string.Empty, result);
        }

        [Theory]
        [InlineData("SET STATISTICS IO ON;\r\nSELECT * FROM dbo.Resource", "SET STATISTICS IO")]
        [InlineData("SET STATISTICS TIME ON;\r\nSELECT * FROM dbo.Resource", "SET STATISTICS TIME")]
        [InlineData("DECLARE @p0 int = 100\r\nDECLARE @p1 smallint = 79\r\nSELECT * FROM dbo.Resource", "DECLARE")]
        [InlineData("SELECT * FROM dbo.Resource\r\nOPTION (RECOMPILE)", "OPTION (RECOMPILE)")]
        [InlineData("SELECT * FROM dbo.Resource\r\n-- execution timeout = 30 sec.", "-- execution timeout")]
        public void StripQueryPreambleLines_RemovesUnwantedLines(string input, string unwantedText)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.DoesNotContain(unwantedText, result);
            Assert.Contains("SELECT * FROM dbo.Resource", result);
        }

        [Theory]
        [InlineData(";WITH cte0 AS (SELECT 1)")]
        [InlineData(";WITH cte0 AS (SELECT 1)\r\nSELECT * FROM cte0")]
        public void StripQueryPreambleLines_ReplacesSemicolonWith(string input)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.DoesNotContain(";WITH", result);
            Assert.StartsWith("WITH", result);
        }

        [Theory]
        [InlineData("set statistics io on;\r\ndeclare @p0 int = 100\r\nSELECT 1", "set statistics")]
        [InlineData("set statistics io on;\r\ndeclare @p0 int = 100\r\nSELECT 1", "declare")]
        public void StripQueryPreambleLines_CaseInsensitive(string input, string unwantedText)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.DoesNotContain(unwantedText, result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SELECT 1", result);
        }

        [Theory]
        [InlineData("DECLARE @p0 int = 100\nSELECT * FROM dbo.Resource")]
        [InlineData("DECLARE @p0 int = 100\r\nSELECT * FROM dbo.Resource")]
        public void StripQueryPreambleLines_HandlesBothNewlineStyles(string input)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.DoesNotContain("DECLARE", result);
            Assert.Contains("SELECT * FROM dbo.Resource", result);
        }

        [Theory]
        [InlineData("DECLARE @p0 int = 100\r\n\r\nWITH cte0 AS (SELECT 1)", "WITH", true)]
        [InlineData("SELECT * FROM dbo.Resource\r\nOPTION (RECOMPILE)\r\n-- execution timeout = 30 sec.\r\n", "dbo.Resource", false)]
        public void StripQueryPreambleLines_TrimsLeadingAndTrailingPreamble(string input, string expected, bool isPrefix)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            if (isPrefix)
            {
                Assert.StartsWith(expected, result);
            }
            else
            {
                Assert.EndsWith(expected, result);
            }
        }

        [Theory]
        [InlineData("SELECT * FROM dbo.Resource WHERE ResourceTypeId = 103", "SELECT * FROM dbo.Resource WHERE ResourceTypeId = 103")]
        [InlineData("SELECT 'DECLARE @x int' FROM dbo.Resource", "SELECT 'DECLARE @x int' FROM dbo.Resource")]
        public void StripQueryPreambleLines_NonPreambleContent_PassesThroughUnchanged(string input, string expected)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.Contains(expected, result);
        }

        [Fact]
        public void StripQueryPreambleLines_OnlyStrippedLines_ReturnsEmpty()
        {
            // Arrange
            string input =
                "SET STATISTICS IO ON;\r\n" +
                "SET STATISTICS TIME ON;\r\n" +
                "DECLARE @p0 int = 100\r\n" +
                "OPTION (RECOMPILE)\r\n" +
                "-- execution timeout = 30 sec.";

            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void StripQueryPreambleLines_MultiStatementBatch_PreservesBothParts()
        {
            // Arrange
            string input =
                "DECLARE @p0 int = 11\r\n" +
                "WITH\r\n" +
                "cte0 AS\r\n" +
                "(\r\n" +
                "    SELECT ResourceTypeId AS T1\r\n" +
                "    FROM dbo.Resource\r\n" +
                ")\r\n" +
                "INSERT INTO @FilteredData SELECT T1 FROM cte0\r\n" +
                "WITH cte1 AS (SELECT * FROM @FilteredData)\r\n" +
                "SELECT * FROM cte1";

            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.DoesNotContain("DECLARE", result);
            Assert.Contains("INSERT INTO @FilteredData", result);
            Assert.Contains("WITH cte1 AS", result);
            Assert.Contains("SELECT * FROM cte1", result);
        }

        [Fact]
        public void StripQueryPreambleLines_PreservesQueryBody()
        {
            // Arrange
            string input =
                "SET STATISTICS IO ON;\r\n" +
                "SET STATISTICS TIME ON;\r\n" +
                "\r\n" +
                "DECLARE @p0 int = 11\r\n" +
                "DECLARE @p1 smallint = 103\r\n" +
                ";WITH\r\n" +
                "cte0 AS\r\n" +
                "(\r\n" +
                "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\r\n" +
                "    FROM dbo.TokenSearchParam\r\n" +
                "    WHERE ResourceTypeId = 103\r\n" +
                ")\r\n" +
                "SELECT * FROM cte0\r\n" +
                "OPTION (RECOMPILE)\r\n" +
                "-- execution timeout = 30 sec.";

            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.StartsWith("WITH", result);
            Assert.Contains("cte0 AS", result);
            Assert.Contains("SELECT ResourceTypeId AS T1", result);
            Assert.Contains("FROM dbo.TokenSearchParam", result);
            Assert.Contains("SELECT * FROM cte0", result);
            Assert.DoesNotContain("SET STATISTICS", result);
            Assert.DoesNotContain("DECLARE", result);
            Assert.DoesNotContain("OPTION (RECOMPILE)", result);
            Assert.DoesNotContain("-- execution timeout", result);
        }

        [Theory]
        [InlineData("WITH cte0 AS (SELECT 1) SELECT * FROM cte0")]
        [InlineData("SELECT 1")]
        [InlineData("WITH cte0 AS (SELECT 1) INSERT INTO @FilteredData SELECT T1 FROM cte0")]
        [InlineData("WITH cte0 AS (SELECT 1)\r\nINSERT INTO @FilteredData SELECT T1 FROM cte0\r\n")]
        public void BatchSplit_NoSecondBatch_ReturnsSingleFragment(string queryText)
        {
            // Act
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(queryText);

            // Assert
            Assert.Single(fragments);
        }

        [Theory]
        [InlineData(
            "WITH cte0 AS (SELECT 1)\r\nINSERT INTO @FilteredData SELECT T1 FROM cte0\r\nWITH cte1 AS (SELECT * FROM @FilteredData)\r\nSELECT * FROM cte1",
            "FROM cte0",
            "WITH cte1 AS")]
        [InlineData(
            "WITH cte0 AS (SELECT 1)\nINSERT INTO @FilteredData SELECT T1 FROM cte0\nWITH cte1 AS (SELECT * FROM @FilteredData)\nSELECT * FROM cte1",
            "FROM cte0",
            "WITH cte1 AS")]
        [InlineData(
            "WITH cte0 AS (SELECT 1)\r\ninsert into @FilteredData SELECT T1 FROM cte0\r\nWITH cte1 AS (SELECT * FROM @FilteredData)\r\nSELECT * FROM cte1",
            "FROM cte0",
            "WITH cte1 AS")]
        [InlineData(
            "INSERT INTO @FilteredData SELECT T1 FROM cte0\r\nWITH cte1 AS (SELECT 1) SELECT * FROM cte1",
            "FROM cte0",
            "WITH cte1 AS")]
        public void BatchSplit_WithInsert_ReturnsTwoFragmentsWithCorrectBoundaries(string queryText, string fragment1EndsWith, string fragment2StartsWith)
        {
            // Act
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(queryText);

            // Assert
            Assert.Equal(2, fragments.Count);
            Assert.EndsWith(fragment1EndsWith, fragments[0]);
            Assert.StartsWith(fragment2StartsWith, fragments[1]);
            Assert.DoesNotContain("INSERT INTO @FilteredData", fragments[1]);
        }

        [Fact]
        public void BatchSplit_Fragment1_ContainsFullInsertLineAndDoesNotLeakIntoSecondBatch()
        {
            // Arrange
            string queryText =
                "WITH cte0 AS (SELECT 1)\r\n" +
                "INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1\r\n" +
                "WITH cte1 AS (SELECT * FROM @FilteredData)\r\n" +
                "SELECT * FROM cte1";

            // Act
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(queryText);

            // Assert
            Assert.Equal(2, fragments.Count);
            Assert.Contains("INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1", fragments[0]);
            Assert.DoesNotContain("WITH cte1 AS (SELECT * FROM @FilteredData)", fragments[0]);
            Assert.StartsWith("WITH cte1 AS (SELECT * FROM @FilteredData)", fragments[1]);
            Assert.Contains("SELECT * FROM cte1", fragments[1]);
        }

        [Fact]
        public void BatchSplit_Truncation_LimitsTo4000Chars()
        {
            // Arrange
            string longCte = "WITH cte0 AS (SELECT " + new string('X', 5000) + ")";
            string queryText = longCte + "\r\nSELECT * FROM cte0";

            // Act
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(queryText);

            // Simulate the truncation applied in LogQueryStoreByTextAsync
            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].Length > 4000)
                {
                    fragments[i] = fragments[i][..4000];
                }
            }

            // Assert
            Assert.Single(fragments);
            Assert.Equal(4000, fragments[0].Length);
        }

        [Fact]
        public void EndToEnd_FullIncludeQuery_NormalizesAndSplitsCorrectly()
        {
            // Arrange — full realistic include query as it comes from SqlQueryGenerator
            string input =
                "SET STATISTICS IO ON;\r\n" +
                "SET STATISTICS TIME ON;\r\n" +
                "\r\n" +
                "DECLARE @p0 int = 11\r\n" +
                "DECLARE @p1 int = 1000\r\n" +
                "DECLARE @p2 int = 11\r\n" +
                "DECLARE @p3 int = 1001\r\n" +
                "DECLARE @p4 int = 1000\r\n" +
                ";WITH\r\n" +
                "cte0 AS\r\n" +
                "(\r\n" +
                "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\r\n" +
                "    FROM dbo.Resource\r\n" +
                "    WHERE IsHistory = 0\r\n" +
                "        AND IsDeleted = 0\r\n" +
                "        AND ResourceTypeId = 79\r\n" +
                ")\r\n" +
                ",cte1 AS\r\n" +
                "(\r\n" +
                "    SELECT row_number() OVER (ORDER BY T1 ASC, Sid1 ASC) AS Row, *\r\n" +
                "    FROM\r\n" +
                "    (\r\n" +
                "        SELECT DISTINCT TOP (@p0) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial\r\n" +
                "        FROM cte0\r\n" +
                "        ORDER BY T1 ASC, Sid1 ASC\r\n" +
                "    ) t\r\n" +
                ")\r\n" +
                "\r\n" +
                "INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1\r\n" +
                "WITH cte1 AS (SELECT * FROM @FilteredData)\r\n" +
                ",cte2 AS\r\n" +
                "(\r\n" +
                "    SELECT DISTINCT TOP (@p1) refTarget.ResourceTypeId AS T1\r\n" +
                "    FROM dbo.ReferenceSearchParam refSource\r\n" +
                ")\r\n" +
                "SELECT * FROM cte2\r\n" +
                "OPTION (RECOMPILE)\r\n" +
                "-- execution timeout = 30 sec.";

            // Act
            string normalized = InvokeStripQueryPreambleLines(input);
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(normalized);

            // Assert — normalization
            Assert.DoesNotContain("SET STATISTICS", normalized);
            Assert.DoesNotContain("DECLARE", normalized);
            Assert.DoesNotContain("OPTION (RECOMPILE)", normalized);
            Assert.DoesNotContain("-- execution timeout", normalized);
            Assert.DoesNotContain(";WITH", normalized);
            Assert.StartsWith("WITH", normalized);

            // Assert — two fragments with correct boundaries
            Assert.Equal(2, fragments.Count);
            Assert.StartsWith("WITH", fragments[0]);
            Assert.Contains("cte0 AS", fragments[0]);
            Assert.Contains("INSERT INTO @FilteredData SELECT T1, Sid1, IsMatch, IsPartial, Row FROM cte1", fragments[0]);
            Assert.DoesNotContain("WITH cte1 AS (SELECT * FROM @FilteredData)", fragments[0]);
            Assert.StartsWith("WITH cte1 AS (SELECT * FROM @FilteredData)", fragments[1]);
            Assert.Contains("SELECT * FROM cte2", fragments[1]);
            Assert.DoesNotContain("INSERT INTO @FilteredData", fragments[1]);
        }

        [Fact]
        public void EndToEnd_SimpleQueryWithoutInsert_NormalizesAndReturnsSingleFragment()
        {
            // Arrange
            string input =
                "SET STATISTICS IO ON;\r\n" +
                "SET STATISTICS TIME ON;\r\n" +
                "\r\n" +
                "DECLARE @p0 int = 11\r\n" +
                ";WITH\r\n" +
                "cte0 AS\r\n" +
                "(\r\n" +
                "    SELECT ResourceTypeId AS T1\r\n" +
                "    FROM dbo.Resource\r\n" +
                "    WHERE ResourceTypeId = 79\r\n" +
                ")\r\n" +
                "SELECT * FROM cte0\r\n" +
                "OPTION (RECOMPILE)\r\n" +
                "-- execution timeout = 30 sec.";

            // Act
            string normalized = InvokeStripQueryPreambleLines(input);
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(normalized);

            // Assert
            Assert.Single(fragments);
            Assert.StartsWith("WITH", fragments[0]);
            Assert.Contains("SELECT * FROM cte0", fragments[0]);
            Assert.DoesNotContain("DECLARE", fragments[0]);
            Assert.DoesNotContain("OPTION (RECOMPILE)", fragments[0]);
        }

        [Fact]
        public void EndToEnd_LinuxNewlines_NormalizesAndSplitsCorrectly()
        {
            // Arrange
            string input =
                "DECLARE @p0 int = 11\n" +
                "WITH\n" +
                "cte0 AS (SELECT 1)\n" +
                "INSERT INTO @FilteredData SELECT T1 FROM cte0\n" +
                "WITH cte1 AS (SELECT * FROM @FilteredData)\n" +
                "SELECT * FROM cte1";

            // Act
            string normalized = InvokeStripQueryPreambleLines(input);
            List<string> fragments = SqlServerSearchService.SplitIntoSearchFragments(normalized);

            // Assert
            Assert.Equal(2, fragments.Count);
            Assert.Contains("INSERT INTO @FilteredData SELECT T1 FROM cte0", fragments[0]);
            Assert.StartsWith("WITH cte1 AS", fragments[1]);
        }

        // -----------------------------------------------------------------------
        // Threshold default vs dbo.Parameters override
        // -----------------------------------------------------------------------

        [Fact]
        public void LongRunningThresholdDefault_Is5000Milliseconds()
        {
            // Assert
            Assert.Equal(5000, SqlServerSearchService.LongRunningThresholdMillisecondsDefault);
        }

        [Theory]
        [InlineData("Search.LongRunningQueryDetails.Threshold", nameof(SqlServerSearchService.LongRunningQueryDetailsThresholdId))]
        [InlineData("Search.LongRunningQueryDetails.IsEnabled", nameof(SqlServerSearchService.LongRunningQueryDetailsParameterId))]
        [InlineData("Search.ReuseQueryPlans.IsEnabled", nameof(SqlServerSearchService.ReuseQueryPlansParameterId))]
        public void ParameterIds_MatchExpectedDatabaseKeys(string expectedKey, string fieldName)
        {
            // Arrange
            var actual = typeof(SqlServerSearchService)
                .GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as string;

            // Assert
            Assert.Equal(expectedKey, actual);
        }

        [Fact]
        public void CachedParameter_ReturnsDefaultValue_WhenDatabaseIsUnavailable()
        {
            // Arrange
            var logger = NullLogger<SqlServerSearchService>.Instance;
            var sqlRetryService = CreateThrowingSqlRetryService();

            var cachedParam = new CachedParameter<SqlServerSearchService>(
                SqlServerSearchService.LongRunningQueryDetailsThresholdId,
                SqlServerSearchService.LongRunningThresholdMillisecondsDefault,
                logger);

            // Act
            double value = cachedParam.GetValue(sqlRetryService);

            // Assert
            Assert.Equal(SqlServerSearchService.LongRunningThresholdMillisecondsDefault, value);
        }

        [Fact]
        public void CachedParameter_CachesValue_AcrossMultipleCalls()
        {
            // Arrange
            var logger = NullLogger<SqlServerSearchService>.Instance;
            var sqlRetryService = CreateThrowingSqlRetryService();

            var cachedParam = new CachedParameter<SqlServerSearchService>(
                SqlServerSearchService.LongRunningQueryDetailsThresholdId,
                SqlServerSearchService.LongRunningThresholdMillisecondsDefault,
                logger);

            // Act
            double firstValue = cachedParam.GetValue(sqlRetryService);
            double secondValue = cachedParam.GetValue(sqlRetryService);

            // Assert
            Assert.Equal(firstValue, secondValue);
            Assert.Equal(SqlServerSearchService.LongRunningThresholdMillisecondsDefault, secondValue);
        }

        [Fact]
        public void CachedParameter_Reset_ClearsCache()
        {
            // Arrange
            var logger = NullLogger<SqlServerSearchService>.Instance;
            var sqlRetryService = CreateThrowingSqlRetryService();

            var cachedParam = new CachedParameter<SqlServerSearchService>(
                "TestParam",
                42.0,
                logger);

            cachedParam.GetValue(sqlRetryService);

            // Act
            cachedParam.Reset();
            double value = cachedParam.GetValue(sqlRetryService);

            // Assert
            Assert.Equal(42.0, value);
        }

        // -----------------------------------------------------------------------
        // Feature flag on/off behavior
        // -----------------------------------------------------------------------
        [Theory]
        [InlineData("LongRunningQueryDetails", true)]
        [InlineData("ReuseQueryPlans", false)]
        public void ProcessingFlag_ReturnsConfiguredDefault_WhenDatabaseIsUnavailable(string flagName, bool defaultValue)
        {
            // Arrange
            var logger = NullLogger<SqlServerSearchService>.Instance;
            var sqlRetryService = CreateThrowingSqlRetryService();

            var flag = new ProcessingFlag<SqlServerSearchService>(
                flagName,
                defaultValue,
                logger);

            // Act
            bool isEnabled = flag.IsEnabled(sqlRetryService);

            // Assert
            Assert.Equal(defaultValue, isEnabled);
        }

        [Fact]
        public void ProcessingFlag_CachesResult_AcrossMultipleCalls()
        {
            // Arrange
            var logger = NullLogger<SqlServerSearchService>.Instance;
            var sqlRetryService = CreateThrowingSqlRetryService();

            var flag = new ProcessingFlag<SqlServerSearchService>(
                SqlServerSearchService.LongRunningQueryDetailsParameterId,
                true,
                logger);

            // Act
            bool first = flag.IsEnabled(sqlRetryService);
            bool second = flag.IsEnabled(sqlRetryService);

            // Assert
            Assert.Equal(first, second);
        }

        [Fact]
        public void ProcessingFlag_Reset_ClearsCache()
        {
            // Arrange
            var logger = NullLogger<SqlServerSearchService>.Instance;
            var sqlRetryService = CreateThrowingSqlRetryService();

            var flag = new ProcessingFlag<SqlServerSearchService>(
                "TestFlag",
                true,
                logger);

            flag.IsEnabled(sqlRetryService);

            // Act
            flag.Reset();
            bool value = flag.IsEnabled(sqlRetryService);

            // Assert
            Assert.True(value);
        }

        // -----------------------------------------------------------------------
        // Normalization output for representative query texts
        // -----------------------------------------------------------------------

        [Fact]
        public void Normalization_RealisticTokenSearch_StripsAllPreambleAndPreservesBody()
        {
            // Arrange
            string input =
                "SET STATISTICS IO ON;\r\n" +
                "SET STATISTICS TIME ON;\r\n" +
                "\r\n" +
                "DECLARE @p0 int = 11\r\n" +
                "DECLARE @p1 smallint = 103\r\n" +
                "DECLARE @p2 varchar(256) = 'http://example.org'\r\n" +
                ";WITH\r\n" +
                "cte0 AS\r\n" +
                "(\r\n" +
                "    SELECT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\r\n" +
                "    FROM dbo.TokenSearchParam\r\n" +
                "    WHERE ResourceTypeId = 103\r\n" +
                "      AND SearchParamId = 22\r\n" +
                "      AND Code = 'active'\r\n" +
                ")\r\n" +
                ",cte1 AS\r\n" +
                "(\r\n" +
                "    SELECT DISTINCT TOP (@p0) T1, Sid1, 1 AS IsMatch, 0 AS IsPartial\r\n" +
                "    FROM cte0\r\n" +
                "    ORDER BY T1 ASC, Sid1 ASC\r\n" +
                ")\r\n" +
                "SELECT r.ResourceTypeId, r.ResourceId, r.Version, r.IsDeleted, r.ResourceSurrogateId\r\n" +
                "FROM dbo.Resource r\r\n" +
                "JOIN cte1 ON r.ResourceTypeId = cte1.T1 AND r.ResourceSurrogateId = cte1.Sid1\r\n" +
                "OPTION (RECOMPILE)\r\n" +
                "-- execution timeout = 30 sec.";

            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert — preamble stripped
            Assert.DoesNotContain("SET STATISTICS", result);
            Assert.DoesNotContain("DECLARE", result);
            Assert.DoesNotContain("OPTION (RECOMPILE)", result);
            Assert.DoesNotContain("-- execution timeout", result);
            Assert.DoesNotContain(";WITH", result);

            // Assert — query body preserved
            Assert.StartsWith("WITH", result);
            Assert.Contains("cte0 AS", result);
            Assert.Contains("FROM dbo.TokenSearchParam", result);
            Assert.Contains("Code = 'active'", result);
            Assert.Contains("SELECT r.ResourceTypeId", result);
            Assert.Contains("JOIN cte1 ON", result);
        }

        [Fact]
        public void Normalization_ExportQuery_NoCtePreamble_PreservesSelectBody()
        {
            // Arrange
            string input =
                "SET STATISTICS IO ON;\r\n" +
                "SET STATISTICS TIME ON;\r\n" +
                "\r\n" +
                "DECLARE @p0 smallint = 79\r\n" +
                "DECLARE @p1 bigint = 1000000\r\n" +
                "DECLARE @p2 bigint = 2000000\r\n" +
                "SELECT ResourceTypeId, ResourceId, Version, IsDeleted, ResourceSurrogateId,\r\n" +
                "       RequestMethod, IsMatch, IsPartial, IsRawResourceMetaSet,\r\n" +
                "       SearchParamHash, RawResource\r\n" +
                "FROM dbo.Resource\r\n" +
                "WHERE ResourceTypeId = @p0\r\n" +
                "  AND ResourceSurrogateId BETWEEN @p1 AND @p2\r\n" +
                "  AND IsHistory = 0\r\n" +
                "  AND IsDeleted = 0\r\n" +
                "OPTION (RECOMPILE)\r\n" +
                "-- execution timeout = 30 sec.";

            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.DoesNotContain("SET STATISTICS", result);
            Assert.DoesNotContain("DECLARE", result);
            Assert.DoesNotContain("OPTION (RECOMPILE)", result);
            Assert.DoesNotContain("-- execution timeout", result);
            Assert.StartsWith("SELECT ResourceTypeId", result);
            Assert.Contains("FROM dbo.Resource", result);
            Assert.Contains("AND IsHistory = 0", result);
        }

        [Theory]
        [InlineData(
            "DECLARE @p0 int = 11\r\nDECLARE @p1 smallint = 103\r\nDECLARE @p2 varchar(256) = 'test'\r\nDECLARE @p3 bigint = 123456789\r\nDECLARE @p4 datetime2 = '2024-01-01'\r\nSELECT 1",
            "SELECT 1")]
        [InlineData(
            "Set Statistics IO ON;\r\nset STATISTICS time ON;\r\nDeclare @p0 int = 100\r\ndeclare @p1 int = 200\r\nSELECT 1\r\noption (recompile)",
            "SELECT 1")]
        public void Normalization_PreambleOnlyVariants_StripsEverythingExceptBody(string input, string expected)
        {
            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Normalization_ConsecutiveBlankLines_CollapsedToSingle()
        {
            // Arrange
            string input =
                "WITH cte0 AS (SELECT 1)\r\n" +
                "\r\n" +
                "\r\n" +
                "\r\n" +
                "SELECT * FROM cte0";

            // Act
            string result = InvokeStripQueryPreambleLines(input);

            // Assert
            Assert.Contains("WITH cte0 AS (SELECT 1)", result);
            Assert.Contains("SELECT * FROM cte0", result);
            Assert.DoesNotContain("\r\n\r\n\r\n", result);
        }

        private static ISqlRetryService CreateThrowingSqlRetryService()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            sqlRetryService
                .When(x => x.ExecuteSql(
                    Arg.Any<Microsoft.Data.SqlClient.SqlCommand>(),
                    Arg.Any<Func<Microsoft.Data.SqlClient.SqlCommand, System.Threading.CancellationToken, System.Threading.Tasks.Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<System.Threading.CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>()))
                .Do(x => throw new InvalidOperationException("No database"));
            return sqlRetryService;
        }
    }
}
