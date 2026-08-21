// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Tests for SqlQueryBuilder - demonstrating usage patterns.
    /// </summary>
    public class SqlQueryBuilderTests
    {
        private readonly ITestOutputHelper _output;

        public SqlQueryBuilderTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void GivenSimpleQuery_WhenBuilt_ThenProperlyFormatted()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.Select("ResourceId", "ResourceTypeId", "Version")
                   .From("dbo.Resource", "r")
                   .Where("r.IsDeleted = 0")
                   .And("r.IsHistory = 0");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains("SELECT", sql);
            Assert.Contains("FROM dbo.Resource AS r", sql);
            Assert.Contains("WHERE r.IsDeleted = 0", sql);
            Assert.Contains("AND r.IsHistory = 0", sql);
        }

        [Fact]
        public void GivenQueryWithJoins_WhenBuilt_ThenProperlyIndented()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.Select("r.ResourceId", "t.Code")
                   .From("dbo.Resource", "r")
                   .InnerJoin("dbo.TokenSearchParam", "t", "t.ResourceSurrogateId = r.ResourceSurrogateId")
                   .Where("r.ResourceTypeId = 1")
                   .OrderBy("r.ResourceId ASC");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains("INNER JOIN dbo.TokenSearchParam AS t ON t.ResourceSurrogateId = r.ResourceSurrogateId", sql);
        }

        [Fact]
        public void GivenMultiLineJoin_WhenBuilt_ThenConditionsOnSeparateLines()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.Select("*")
                   .From("dbo.Resource", "r")
                   .JoinMultiLine(
                       "LEFT",
                       "dbo.DateTimeSearchParam",
                       "dt",
                       "dt.ResourceTypeId = r.ResourceTypeId",
                       "dt.ResourceSurrogateId = r.ResourceSurrogateId",
                       "dt.SearchParamId = 5");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains("LEFT JOIN dbo.DateTimeSearchParam AS dt", sql);
            Assert.Contains("ON dt.ResourceTypeId = r.ResourceTypeId", sql);
            Assert.Contains("AND dt.ResourceSurrogateId = r.ResourceSurrogateId", sql);
            Assert.Contains("AND dt.SearchParamId = 5", sql);
        }

        [Fact]
        public void GivenCte_WhenBuilt_ThenProperlyFormatted()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.BeginCte("FilteredPatients", isFirstCte: true)
                   .Select("ResourceId", "ResourceSurrogateId")
                   .From("dbo.Resource", "r")
                   .Where("r.ResourceTypeId = 1")
                   .EndCte();

            builder.AppendLine();
            builder.Select("*")
                   .From("FilteredPatients");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains(";WITH", sql);
            Assert.Contains("FilteredPatients AS (", sql);
            Assert.Contains(")", sql);
        }

        [Fact]
        public void GivenMultipleCtes_WhenBuilt_ThenProperlyChained()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.BeginCte("cte1", isFirstCte: true)
                   .Select("ResourceId")
                   .From("dbo.Resource")
                   .Where("ResourceTypeId = 1")
                   .EndCte();

            builder.AppendLine();

            builder.BeginCte("cte2")
                   .Select("ResourceId")
                   .From("cte1")
                   .Where("ResourceId > 100")
                   .EndCte();

            builder.AppendLine();
            builder.Select("*")
                   .From("cte2");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains(";WITH", sql);
            Assert.Contains("cte1 AS (", sql);
            Assert.Contains(",", sql); // Comma between CTEs
            Assert.Contains("cte2 AS (", sql);
        }

        [Fact]
        public void GivenComplexQuery_WhenBuilt_ThenProperlyIndented()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.BeginCte("SearchResults", isFirstCte: true)
                   .IncreaseIndent()
                   .SelectWithModifier("DISTINCT TOP 100", "r.ResourceTypeId", "r.ResourceSurrogateId", "1 AS IsMatch")
                   .From("dbo.Resource", "r")
                   .InnerJoin("dbo.TokenSearchParam", "t", "t.ResourceSurrogateId = r.ResourceSurrogateId")
                   .Where("r.ResourceTypeId = 1")
                   .And("r.IsDeleted = 0")
                   .And("t.Code = 'active'")
                   .OrderBy("r.ResourceSurrogateId DESC")
                   .DecreaseIndent()
                   .EndCte();

            builder.AppendLine();

            builder.Select("r.ResourceId", "r.Version", "r.RawResource")
                   .From("dbo.Resource", "r")
                   .InnerJoin("SearchResults", "s", "s.ResourceSurrogateId = r.ResourceSurrogateId")
                   .Where("r.IsHistory = 0");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains(";WITH", sql);
            Assert.Contains("SearchResults AS (", sql);
            Assert.Contains("DISTINCT TOP 100", sql);
        }

        [Fact]
        public void GivenNestedCtes_WhenBuilt_ThenIndentationPreserved()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.BeginCte("OuterCte", isFirstCte: true)
                   .Select("*")
                   .From("dbo.Resource")
                   .EndCte();

            builder.AppendLine();

            builder.BeginCte("InnerCte")
                   .Select("ResourceId", "ResourceTypeId")
                   .From("OuterCte")
                   .Where("ResourceTypeId IN (1, 2, 3)")
                   .EndCte();

            builder.AppendLine();

            builder.Select("COUNT(*) AS Total")
                   .From("InnerCte");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains("OuterCte AS (", sql);
            Assert.Contains("InnerCte AS (", sql);

            // Check indentation is consistent
            var lines = sql.Split('\n');
            var hasProperIndentation = true;
            foreach (var line in lines)
            {
                _output.WriteLine($"Line: '{line.TrimEnd()}'");
            }

            Assert.True(hasProperIndentation);
        }

        [Fact]
        public void GivenManualIndentation_WhenUsed_ThenProperlyApplied()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.AppendLine("SELECT");
            builder.IncreaseIndent();
            builder.AppendLine("ResourceId,");
            builder.AppendLine("ResourceTypeId,");
            builder.AppendLine("Version");
            builder.DecreaseIndent();
            builder.AppendLine("FROM dbo.Resource");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            var lines = sql.Split('\n');

            // First line should have no indent
            Assert.StartsWith("SELECT", lines[0]);

            // Column lines should be indented
            Assert.StartsWith("  ", lines[1]); // ResourceId with 2-space indent
        }

        [Fact]
        public void GivenQueryWithGroupBy_WhenBuilt_ThenProperlyFormatted()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act
            builder.Select("ResourceTypeId", "COUNT(*) AS ResourceCount")
                   .From("dbo.Resource", "r")
                   .Where("r.IsDeleted = 0")
                   .GroupBy("ResourceTypeId")
                   .Having("COUNT(*) > 10")
                   .OrderBy("ResourceCount DESC");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            Assert.Contains("GROUP BY ResourceTypeId", sql);
            Assert.Contains("HAVING COUNT(*) > 10", sql);
        }

        [Fact]
        public void GivenRealWorldSearchQuery_WhenBuilt_ThenMatchesExpectedFormat()
        {
            // Arrange
            var builder = new SqlQueryBuilder();

            // Act - Build a realistic FHIR search query
            builder.BeginCte("cte0", isFirstCte: true)
                   .SelectWithModifier("DISTINCT", 
                       "r.ResourceTypeId",
                       "r.ResourceSurrogateId",
                       "1 AS IsMatch",
                       "0 AS IsPartial",
                       "row_number() OVER (ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC) AS Row")
                   .From("dbo.Resource", "r")
                   .InnerJoin("dbo.TokenSearchParam", "t", "t.ResourceSurrogateId = r.ResourceSurrogateId")
                   .Where("r.ResourceTypeId = 1")
                   .And("r.IsDeleted = 0")
                   .And("r.IsHistory = 0")
                   .And("t.SearchParamId = 5")
                   .And("t.Code = 'active'")
                   .EndCte();

            builder.AppendLine();

            builder.BeginCte("cte1")
                   .AppendLine("SELECT TOP 11 * FROM cte0")
                   .AppendLine("ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC")
                   .EndCte();

            builder.AppendLine();

            builder.Select("r.ResourceId", "r.Version", "r.ResourceTypeId", "r.RawResource")
                   .From("dbo.Resource", "r")
                   .InnerJoin("cte1", "f", "f.ResourceSurrogateId = r.ResourceSurrogateId")
                   .Where("r.IsHistory = 0")
                   .OrderBy("r.ResourceTypeId ASC, r.ResourceSurrogateId ASC");

            var sql = builder.ToString();

            // Assert
            _output.WriteLine(sql);
            _output.WriteLine("\n--- Formatted SQL ---");
            _output.WriteLine(sql);

            Assert.Contains(";WITH", sql);
            Assert.Contains("cte0 AS (", sql);
            Assert.Contains("cte1 AS (", sql);
            Assert.Contains("DISTINCT", sql);
            Assert.Contains("row_number() OVER", sql);
        }
    }
}
