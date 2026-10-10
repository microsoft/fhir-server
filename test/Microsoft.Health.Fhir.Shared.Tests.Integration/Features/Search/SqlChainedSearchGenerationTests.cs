// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Search
{
    /// <summary>
    /// Executes generated chained-search SQL against connection-local tables, without provisioning a FHIR database.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlChainedSearchGenerationTests
    {
        private readonly ITestOutputHelper _output;

        public SqlChainedSearchGenerationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("same", "Patient", false, false, 1)]
        [InlineData("same", "Group", false, false, 1)]
        [InlineData("same", "Device", false, false, 1)]
        [InlineData("same", "Location", false, false, 1)]
        [InlineData("same", null, false, false, 1)]
        [InlineData("different", "Patient", false, false, 1)]
        [InlineData("split-bounds", "Patient", false, false, 1)]
        [InlineData("bound-same", "Patient", false, false, 1)]
        [InlineData("bound-different", "Patient", false, false, 0)]
        [InlineData("bound-split-bounds", "Patient", false, false, 0)]
        [InlineData("no-date", "Patient", false, false, 0)]
        [InlineData("no-terminal", "Patient", false, false, 0)]
        [InlineData("no-target", "Patient", false, false, 0)]
        [InlineData("cross-source", "Patient", false, false, 0)]
        [InlineData("same", "Practitioner", false, false, 0)]
        [InlineData("same", "Patient", true, false, 0)]
        [InlineData("same", "Patient", false, true, 0)]
        public async Task GivenIndependentChains_WhenExecuted_ThenSourceExistenceSemanticsArePreserved(
            string scenario,
            string terminalType,
            bool history,
            bool deleted,
            int expectedCount)
        {
            // Arrange
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection, scenario, terminalType, history, deleted);
            using var command = CreateSearchCommand(connection, bindTarget: scenario.StartsWith("bound-", StringComparison.Ordinal));
            string improvedSql = command.CommandText;
            _output.WriteLine(improvedSql);

            // Act
            var improvedIds = await ReadIdsAsync(command);
            command.CommandText = RestoreMultiplyingJoins(improvedSql);
            var originalIds = await ReadIdsAsync(command);

            // Assert
            Assert.Equal(expectedCount, improvedIds.Count);
            Assert.Equal(originalIds, improvedIds);
            Assert.Equal(improvedIds.Count, improvedIds.Distinct().Count());
            Assert.All(improvedIds, id => Assert.Equal("source", id));
        }

        [Fact]
        public async Task GivenManyMatchingReferences_WhenChainsRestart_ThenIntermediateRowsDoNotMultiply()
        {
            // Arrange
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection, "same", "Patient", false, false);
            using var command = CreateSearchCommand(connection);
            string sql = command.CommandText;
            int finalSelect = sql.IndexOf(SqlQueryGenerator.ParametersHashStart, StringComparison.Ordinal);
            Assert.True(finalSelect > 0);
            string countsSql = sql[..finalSelect] + "SELECT (SELECT count_big(*) FROM cte3), (SELECT count_big(*) FROM cte5), (SELECT count_big(*) FROM cte7)";

            // Act
            command.CommandText = countsSql;
            var improved = await ReadCountsAsync(command);
            command.CommandText = RestoreMultiplyingJoins(countsSql);
            var original = await ReadCountsAsync(command);

            // Assert
            Assert.All(improved, rows => Assert.Equal(130L, rows));
            Assert.True(original[2] > 2_000_000, $"Expected the original traversal product, got {original[2]} rows.");
            _output.WriteLine($"Traversal rows before: {string.Join(", ", original)}; after: {string.Join(", ", improved)}");
            _output.WriteLine(sql);
        }

        private static string RestoreMultiplyingJoins(string sql) =>
            Regex.Replace(sql, @"JOIN \(SELECT DISTINCT T1, Sid1 FROM (cte\d+)\) predecessor", "JOIN $1");

        private static async Task<List<string>> ReadIdsAsync(SqlCommand command)
        {
            using var reader = await command.ExecuteReaderAsync();
            var ids = new List<string>();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetString(reader.GetOrdinal("ResourceId")));
            }

            return ids;
        }

        private static async Task<long[]> ReadCountsAsync(SqlCommand command)
        {
            using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return [reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2)];
        }

        private static SqlCommand CreateSearchCommand(SqlConnection connection, bool bindTarget = false)
        {
            var model = Substitute.For<ISqlServerFhirModel>();
            model.TryGetSystemId("urn:visit", out Arg.Any<int>()).Returns(call =>
            {
                call[1] = 21;
                return true;
            });
            string[] types = ["DiagnosticReport", "Observation", "Patient", "Group", "Device", "Location", "Practitioner"];
            for (int i = 0; i < types.Length; i++)
            {
                short id = (short)(i + 1);
                model.GetResourceTypeId(types[i]).Returns(id);
                model.TryGetResourceTypeId(types[i], out Arg.Any<short>()).Returns(call =>
                {
                    call[1] = id;
                    return true;
                });
            }

            var initial = Parameter("patient", SearchParamType.Reference, ["Patient"]);
            var status = Parameter("status", SearchParamType.Token);
            var code = Parameter("code", SearchParamType.Token);
            var traversal = Parameter("result", SearchParamType.Reference, ["Observation"]);
            var date = Parameter("date", SearchParamType.Date);
            var terminal = Parameter("subject", SearchParamType.Reference, ["Patient", "Group", "Device", "Location"]);
            SearchParameterInfo[] parameters = [initial, status, code, traversal, date, terminal];
            for (int i = 0; i < parameters.Length; i++)
            {
                model.GetSearchParamId(parameters[i].Url).Returns((short)(i + 10));
            }

            Expression Chain(Expression predicate) => Expression.Chained(["DiagnosticReport"], traversal, ["Observation"], false, predicate);
            Expression[] targetPredicates =
            [
                Expression.SearchParameter(date, Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, DateTimeOffset.Parse("2026-01-02T00:00:00Z"))),
                Expression.SearchParameter(date, Expression.LessThanOrEqual(FieldName.DateTimeStart, null, DateTimeOffset.Parse("2026-01-03T00:00:00Z"))),
                Expression.SearchParameter(terminal, Expression.StringEquals(FieldName.ReferenceResourceId, null, "goal", false)),
            ];
            var predicates = new List<Expression>
            {
                Expression.SearchParameter(initial, Expression.StringEquals(FieldName.ReferenceResourceId, null, "start", false)),
                Expression.SearchParameter(status, Expression.Or(Expression.StringEquals(FieldName.TokenCode, null, "final", false), Expression.StringEquals(FieldName.TokenCode, null, "preliminary", false))),
                Expression.SearchParameter(code, Expression.And(
                    Expression.StringEquals(FieldName.TokenSystem, null, "urn:visit", false),
                    Expression.StringEquals(FieldName.TokenCode, null, "visit", false))),
            };
            if (bindTarget)
            {
                predicates.Add(Chain(Expression.And(targetPredicates)));
            }
            else
            {
                predicates.AddRange(targetPredicates.Select(Chain));
            }

            Expression expression = Expression.And(predicates.ToArray());
            var factory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var root = (SqlRootExpression)expression
                .AcceptVisitor(UntypedReferenceRewriter.Instance)
                .AcceptVisitor(new SqlRootExpressionRewriter(factory))
                .AcceptVisitor(new ChainFlatteningRewriter(factory));
            var tables = root.SearchParamTableExpressions.ToList();
            tables.Add(new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top));
            root = new SqlRootExpression(tables, []);
            var command = connection.CreateCommand();
            var builder = new IndentedStringBuilder(new StringBuilder());
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var generator = new SqlQueryGenerator(builder, new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)), model, schema, factory, false, false);
            generator.VisitSqlRoot(root, new SearchOptions
            {
                Sort = [(new SearchParameterInfo("_lastUpdated", "_lastUpdated"), SortOrder.Ascending)],
                MaxItemCount = 10,
                ResourceVersionTypes = ResourceVersionType.Latest,
            });
            SqlCommandSimplifier.RemoveRedundantParameters(builder, command.Parameters, NullLogger.Instance);
            command.CommandText = builder.ToString()
                .Replace("dbo.ReferenceSearchParam", "#ReferenceSearchParam", StringComparison.Ordinal)
                .Replace("dbo.DateTimeSearchParam", "#DateTimeSearchParam", StringComparison.Ordinal)
                .Replace("dbo.TokenSearchParam", "#TokenSearchParam", StringComparison.Ordinal)
                .Replace("dbo.Resource", "#Resource", StringComparison.Ordinal);
            return command;
        }

        private static SearchParameterInfo Parameter(string name, SearchParamType type, string[] targets = null) =>
            new(name, name, type, new Uri($"http://example.org/SearchParameter/{name}"), null, name, targets);

        private static async Task SeedAsync(SqlConnection connection, string scenario, string terminalType, bool history, bool deleted)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE #Resource (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, ResourceId varchar(64),
                    IsHistory bit, IsDeleted bit, Version int DEFAULT 1, RequestMethod varchar(10),
                    IsRawResourceMetaSet bit, SearchParamHash varchar(64), RawResource varbinary(max));
                CREATE TABLE #ReferenceSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    ReferenceResourceTypeId smallint NULL, ReferenceResourceId varchar(64), IsHistory bit);
                CREATE TABLE #TokenSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    Code varchar(256), IsHistory bit, SystemId int);
                CREATE TABLE #DateTimeSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    StartDateTime datetime2, EndDateTime datetime2, IsHistory bit);
                INSERT INTO #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                    VALUES (1, 100, 'source', 0, 0), (2, 200, 'target-a', @history, @deleted),
                           (2, 201, 'target-b', @history, @deleted), (2, 202, 'unqualified', @history, @deleted);
                INSERT INTO #ReferenceSearchParam VALUES (1, 100, 10, 3, 'start', 0);
                INSERT INTO #TokenSearchParam VALUES (1, 100, 11, 'final', 0, NULL), (1, 100, 12, 'visit', 0, 21);
                WITH numbers AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM numbers WHERE n < 128)
                INSERT INTO #ReferenceSearchParam SELECT 1, 100, 13, 2, 'target-a', 0 FROM numbers OPTION (MAXRECURSION 128);
                INSERT INTO #ReferenceSearchParam VALUES (1, 100, 13, 2, 'target-b', 0), (1, 100, 13, 2, 'unqualified', 0);
                INSERT INTO #DateTimeSearchParam VALUES
                    (2, 200, 14, @start, '2026-01-04', @history),
                    (2, 201, 14, '2026-01-01', '2026-01-01', @history);
                INSERT INTO #ReferenceSearchParam VALUES (2, @terminalSid, 15, @terminalType, 'goal', @history);
                IF @scenario = 'no-date' DELETE FROM #DateTimeSearchParam;
                IF @scenario = 'no-terminal' DELETE FROM #ReferenceSearchParam WHERE SearchParamId = 15;
                IF @scenario = 'no-target' DELETE FROM #Resource WHERE ResourceTypeId = 2;
                IF @history = 1
                    INSERT INTO #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                        VALUES (2, 203, 'target-a', 0, 0);
                IF @scenario = 'cross-source'
                BEGIN
                    DELETE FROM #ReferenceSearchParam WHERE ResourceTypeId = 1 AND ReferenceResourceId = 'target-b';
                    INSERT INTO #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                        VALUES (1, 101, 'other-source', 0, 0);
                    INSERT INTO #ReferenceSearchParam VALUES (1, 101, 10, 3, 'start', 0), (1, 101, 13, 2, 'target-b', 0);
                    INSERT INTO #TokenSearchParam VALUES (1, 101, 11, 'final', 0, NULL), (1, 101, 12, 'visit', 0, 21);
                END
                """;

            // Create temp tables in the session batch, not the parameterized sp_executesql scope.
            int seedStart = command.CommandText.IndexOf("INSERT INTO #Resource", StringComparison.Ordinal);
            string seedSql = command.CommandText[seedStart..];
            command.CommandText = command.CommandText[..seedStart];
            await command.ExecuteNonQueryAsync();
            command.CommandText = seedSql;
            command.Parameters.AddWithValue("@history", history);
            command.Parameters.AddWithValue("@deleted", deleted);
            command.Parameters.AddWithValue("@start", scenario.EndsWith("split-bounds", StringComparison.Ordinal) ? new DateTime(2026, 1, 4) : new DateTime(2026, 1, 1));
            command.Parameters.AddWithValue("@terminalSid", scenario.EndsWith("different", StringComparison.Ordinal) || scenario == "cross-source" ? 201L : 200L);
            string[] types = ["DiagnosticReport", "Observation", "Patient", "Group", "Device", "Location", "Practitioner"];
            command.Parameters.Add("@terminalType", System.Data.SqlDbType.SmallInt).Value =
                terminalType == null ? DBNull.Value : (object)(short)(Array.IndexOf(types, terminalType) + 1);
            command.Parameters.AddWithValue("@scenario", scenario);
            await command.ExecuteNonQueryAsync();
        }
    }
}
