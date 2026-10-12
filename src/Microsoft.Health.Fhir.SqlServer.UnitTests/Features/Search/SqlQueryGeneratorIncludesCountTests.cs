// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    /// <summary>
    /// Verifies the SQL shape generated for <c>$includes?_summary=count</c>.
    /// </summary>
    /// <remarks>
    /// The count is the number of distinct included resources of the whole original search. It must ignore paging
    /// (match page, include cursor, page sizes), limits, sort and sort phases.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlQueryGeneratorIncludesCountTests : IClassFixture<ModelInfoProviderFixture>
    {
        // Match page replay (size 3), include cursor (type 10, surrogate id 250) and second sort phase.
        private const string PagedIncludesToken = "[10,100,300,10,250,true,null,null,3]";

        private readonly ISqlServerFhirModel _fhirModel = Substitute.For<ISqlServerFhirModel>();
        private readonly SearchParamTableExpressionQueryGeneratorFactory _queryGeneratorFactory = new(new SearchParameterToSearchValueTypeMap());
        private readonly SchemaInformation _schemaInformation = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max);

        public SqlQueryGeneratorIncludesCountTests(ModelInfoProviderFixture modelInfoProviderFixture)
        {
            _ = modelInfoProviderFixture;
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Theory]
        [InlineData(10, 11)]
        [InlineData(int.MaxValue, int.MaxValue)]
        public void GivenIncludesCount_WhenSqlGenerated_ThenNothingIsTruncatedPagedOrSorted(int includeCount, int maxItemCount)
        {
            // Arrange
            var expression = CreateIncludesExpression(CreateInclude("Patient", "Observation", reversed: false, iterate: false), CreateInclude("Patient", "Practitioner", reversed: true, iterate: false));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, includeCount, maxItemCount);

            // Act
            (string sql, SqlParameterCollectionSnapshot parameters) = Generate(expression, options);

            // Assert
            Assert.DoesNotContain("TOP", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("SortValue", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("count_big(DISTINCT Sid1)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain(parameters.Values, v => v is int i && (i == includeCount + 1 || i == maxItemCount + 1 || i == 3));
        }

        [Fact]
        public void GivenIncludesCount_WhenSqlGenerated_ThenIncludedResourcesAreCountedDistinctlyByTypeAndSurrogateId()
        {
            // Arrange
            var expression = CreateIncludesExpression(CreateInclude("Patient", "Observation", reversed: false, iterate: false), CreateInclude("Patient", "Practitioner", reversed: true, iterate: false));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, 10, 11);

            // Act
            (string sql, _) = Generate(expression, options);

            // Assert
            Assert.Contains("SELECT count_big(*)", sql, StringComparison.Ordinal);
            Assert.Matches(@"FROM \(SELECT DISTINCT T1, Sid1 FROM cte\d+\) AS IncludedResources", sql);
        }

        [Fact]
        public void GivenIncludesCount_WhenSqlGenerated_ThenEveryIncludeCteExcludesMatchesAndMatchesAreNotCounted()
        {
            // Arrange
            var expression = CreateIncludesExpression(CreateInclude("Patient", "Observation", reversed: false, iterate: false), CreateInclude("Patient", "Practitioner", reversed: true, iterate: false));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, 10, 11);

            // Act
            (string sql, _) = Generate(expression, options);

            // Assert
            Assert.Equal(2, Regex.Matches(sql, @"WHERE NOT EXISTS \(SELECT \* FROM cte\d+ WHERE cte\d+\.Sid1 = cte\d+\.Sid1 AND cte\d+\.T1 = cte\d+\.T1\)").Count);
            Assert.Single(Regex.Matches(sql, "UNION ALL"));
        }

        [Fact]
        public void GivenIncludesCountWithIncludeCursor_WhenSqlGenerated_ThenCursorAndMatchPageAreIgnored()
        {
            // Arrange
            var expression = CreateIncludesExpression(CreateInclude("Patient", "Observation", reversed: false, iterate: false));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, 10, 11);

            // Act
            (string sql, SqlParameterCollectionSnapshot parameters) = Generate(expression, options);

            // Assert
            Assert.DoesNotContain("> 250", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("> 10 OR", sql, StringComparison.Ordinal);
            Assert.DoesNotContain(parameters.Values, v => v is int i && i == 3);
        }

        [Fact]
        public void GivenIncludesCountWithSort_WhenSqlGenerated_ThenNoSortColumnIsReferenced()
        {
            // Arrange
            var expression = CreateIncludesExpression(CreateInclude("Patient", "Observation", reversed: false, iterate: false));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, 10, 11);
            options.Sort = new List<(SearchParameterInfo, SortOrder)>
            {
                (new SearchParameterInfo("family", "family", SearchParamType.String), SortOrder.Descending),
                (new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Ascending),
            };

            // Act
            (string sql, _) = Generate(expression, options);

            // Assert
            Assert.DoesNotContain("SortValue", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenIncludesCountWithIterateInclude_WhenSqlGenerated_ThenIterateReadsFromThePrecedingIncludeCte()
        {
            // Arrange
            var expression = CreateIncludesExpression(
                CreateInclude("Patient", "Observation", reversed: false, iterate: false),
                CreateInclude("Observation", "Practitioner", reversed: false, iterate: true));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, 10, 11);

            // Act
            (string sql, _) = Generate(expression, options);

            // Assert
            Assert.Matches(@"(?s),cte3 AS.*?EXISTS \(SELECT \* FROM cte2 WHERE refSource\.ResourceTypeId = T1 AND refSource\.ResourceSurrogateId = Sid1\)\s*\)\s*,cte4 AS", sql);
            Assert.DoesNotContain("SELECT * FROM cte3 WHERE", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenIncludesCountWithIterateAndWildcardIncludes_WhenSqlGenerated_ThenAllIncludeCtesAreCountedWithoutTruncation()
        {
            // Arrange
            var expression = CreateIncludesExpression(
                CreateInclude("Patient", "Observation", reversed: false, iterate: false),
                CreateInclude("Observation", "Practitioner", reversed: false, iterate: true),
                CreateWildcardInclude("Patient", reversed: false),
                CreateWildcardInclude("Patient", reversed: true));
            var options = CreateOptions(countOnly: true, PagedIncludesToken, 10, 11);

            // Act
            (string sql, _) = Generate(expression, options);

            // Assert
            Assert.DoesNotContain("TOP", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("ORDER BY", sql, StringComparison.Ordinal);
            Assert.Equal(4, Regex.Matches(sql, @"WHERE NOT EXISTS \(SELECT \* FROM").Count);
        }

        [Fact]
        public void GivenIncludesOperationWithoutCount_WhenSqlGenerated_ThenPagingBehaviorIsUnchanged()
        {
            // Arrange
            var expression = CreateIncludesExpression(CreateInclude("Patient", "Observation", reversed: false, iterate: false));
            var options = CreateOptions(countOnly: false, PagedIncludesToken, 10, 11);

            // Act
            (string sql, SqlParameterCollectionSnapshot parameters) = Generate(expression, options);

            // Assert
            Assert.Contains("TOP (", sql, StringComparison.Ordinal);
            Assert.Contains("ORDER BY T1 ASC, Sid1 ASC", sql, StringComparison.Ordinal);
            Assert.Contains("> 250", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT count_big(", sql, StringComparison.Ordinal);
            Assert.Contains(parameters.Values, v => v is int i && i == 3);
        }

        [Fact]
        public void GivenRegularCountSearch_WhenSqlGenerated_ThenCountShapeIsUnchanged()
        {
            // Arrange
            SqlRootExpression expression = new(
                [
                    new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.All),
                ],
                []);
            var options = CreateOptions(countOnly: true, null, 10, 11);

            // Act
            (string sql, _) = Generate(expression, options);

            // Assert
            Assert.Contains("count_big(DISTINCT Sid1)", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("IncludedResources", sql, StringComparison.Ordinal);
        }

        private static SearchOptions CreateOptions(bool countOnly, string includesToken, int includeCount, int maxItemCount)
        {
            return new SearchOptions
            {
                CountOnly = countOnly,
                IncludesContinuationToken = includesToken,
                IncludeCount = includeCount,
                MaxItemCount = maxItemCount,
                Sort = [],
                ResourceVersionTypes = ResourceVersionType.Latest,
            };
        }

        private static SqlRootExpression CreateIncludesExpression(params IncludeExpression[] includes)
        {
            var tables = new List<SearchParamTableExpression>
            {
                new(null, null, SearchParamTableExpressionKind.All),
                new(null, null, SearchParamTableExpressionKind.Top),
            };
            tables.AddRange(includes.Select(i => new SearchParamTableExpression(IncludeQueryGenerator.Instance, i, SearchParamTableExpressionKind.Include)));

            return (SqlRootExpression)new SqlRootExpression(tables, []).AcceptVisitor(IncludesOperationRewriter.Instance, null);
        }

        private static IncludeExpression CreateInclude(string sourceType, string targetType, bool reversed, bool iterate)
        {
            var parameter = new SearchParameterInfo("reference", "reference", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/reference"), null, null, [targetType]);

            return new IncludeExpression([sourceType], parameter, sourceType, targetType, null, wildCard: false, reversed: reversed, iterate: iterate);
        }

        private static IncludeExpression CreateWildcardInclude(string sourceType, bool reversed)
        {
            return new IncludeExpression([sourceType], null, sourceType, null, ["Observation", "Practitioner"], wildCard: true, reversed: reversed, iterate: false);
        }

        private (string Sql, SqlParameterCollectionSnapshot Parameters) Generate(SqlRootExpression expression, SearchOptions options)
        {
            using Data.SqlClient.SqlCommand command = new();
            var builder = new IndentedStringBuilder(new StringBuilder());
            var generator = new SqlQueryGenerator(
                builder,
                new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                _fhirModel,
                _schemaInformation,
                _queryGeneratorFactory,
                false,
                false);

            generator.VisitSqlRoot(expression, options);

            return (builder.ToString(), new SqlParameterCollectionSnapshot(command.Parameters.Cast<Microsoft.Data.SqlClient.SqlParameter>().Select(p => p.Value).ToList()));
        }

        private sealed record SqlParameterCollectionSnapshot(IReadOnlyList<object> Values);
    }
}
