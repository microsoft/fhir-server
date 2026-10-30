// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
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
    /// Executes generated exact semantic-search SQL against connection-local tables, without provisioning a FHIR database.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait("DataStore", "SqlServer")]
    public class SqlSemanticSearchGenerationTests
    {
        private const short ObservationTypeId = 1;
        private const short PatientTypeId = 2;
        private const short DiagnosticReportTypeId = 3;
        private const short SemanticSearchParamId = 20;
        private const short EmbeddingModelId = 3;

        private static readonly SearchParameterInfo SemanticParameter = Parameter("semantic-text", SearchParamType.Special);
        private static readonly SearchParameterInfo SubjectParameter = Parameter("subject", SearchParamType.Reference, ["Patient"]);
        private static readonly SearchParameterInfo ResultParameter = Parameter("result", SearchParamType.Reference, ["Observation"]);
        private static readonly SearchParameterInfo HasMemberParameter = Parameter("has-member", SearchParamType.Reference, ["Observation"]);
        private static readonly SearchParameterInfo LinkParameter = Parameter("link", SearchParamType.Reference, ["Patient"]);
        private static readonly SearchParameterInfo LastUpdatedParameter = new("_lastUpdated", "_lastUpdated");

        private readonly ITestOutputHelper _output;

        static SqlSemanticSearchGenerationTests()
        {
            ConfigureWorkloadIdentityAuthentication();
        }

        public SqlSemanticSearchGenerationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GivenSemanticInclude_WhenExecuted_ThenOnlyReturnedRankedMatchesAreExpanded()
        {
            // Arrange
            using SqlConnection connection = await OpenSeededConnectionAsync();
            var include = new IncludeExpression(["Observation"], SubjectParameter, "Observation", "Patient", null, false, false, false);
            using SqlCommand command = CreateSearchCommand(connection, include, CreateOptions(maxItemCount: 2));

            // Act
            List<SearchRow> rows = await ReadRowsAsync(command);

            // Assert - relevance order differs from surrogate order, and the lookahead match is not expanded.
            Assert.Equal(["obs-near", "obs-mid", "obs-far"], rows.Where(r => r.IsMatch).Select(r => r.Id));
            AssertRankedDistances(rows);
            Assert.Equal(["patient-mid", "patient-near"], rows.Where(r => !r.IsMatch).Select(r => r.Id).Order());
            Assert.All(rows.Where(r => !r.IsMatch), r => Assert.Null(r.Distance));
            Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
        }

        [Fact]
        public async Task GivenSemanticRevInclude_WhenExecuted_ThenResourcesWithoutEmbeddingsAreIncluded()
        {
            // Arrange
            using SqlConnection connection = await OpenSeededConnectionAsync();
            var revInclude = new IncludeExpression(["Observation"], ResultParameter, "DiagnosticReport", "Observation", null, false, true, false);
            using SqlCommand command = CreateSearchCommand(connection, revInclude, CreateOptions(maxItemCount: 10));

            // Act
            List<SearchRow> rows = await ReadRowsAsync(command);

            // Assert
            Assert.Equal(["obs-near", "obs-mid", "obs-far"], rows.Where(r => r.IsMatch).Select(r => r.Id));
            AssertRankedDistances(rows);
            SearchRow report = Assert.Single(rows, r => !r.IsMatch);
            Assert.Equal("report-near", report.Id);
            Assert.Null(report.Distance);
        }

        [Fact]
        public async Task GivenSemanticIncludeOfAnotherMatch_WhenExecuted_ThenTheResourceIsReturnedOnceAsAMatch()
        {
            // Arrange
            using SqlConnection connection = await OpenSeededConnectionAsync();
            var include = new IncludeExpression(["Observation"], HasMemberParameter, "Observation", "Observation", null, false, false, false);
            using SqlCommand command = CreateSearchCommand(connection, include, CreateOptions(maxItemCount: 10));

            // Act
            List<SearchRow> rows = await ReadRowsAsync(command);

            // Assert
            Assert.Equal(["obs-near", "obs-mid", "obs-far"], rows.Select(r => r.Id));
            Assert.All(rows, r => Assert.True(r.IsMatch));
            AssertRankedDistances(rows);
        }

        [Fact]
        public async Task GivenSemanticIncludeWithLastUpdatedSort_WhenExecuted_ThenMatchesArePagedBySortBeforeExpansion()
        {
            // Arrange
            using SqlConnection connection = await OpenSeededConnectionAsync();
            var include = new IncludeExpression(["Observation"], SubjectParameter, "Observation", "Patient", null, false, false, false);
            using SqlCommand command = CreateSearchCommand(connection, include, CreateOptions(maxItemCount: 2, sort: [(LastUpdatedParameter, SortOrder.Ascending)]));

            // Act
            List<SearchRow> rows = await ReadRowsAsync(command);

            // Assert - the explicit sort still pages only semantic matches, and every match keeps its score.
            Assert.Equal(["obs-far", "obs-mid", "obs-near"], rows.Where(r => r.IsMatch).Select(r => r.Id));
            Assert.All(rows.Where(r => r.IsMatch), r => Assert.NotNull(r.Distance));
            Assert.Equal(["patient-far", "patient-mid"], rows.Where(r => !r.IsMatch).Select(r => r.Id).Order());
        }

        [Fact]
        public async Task GivenSemanticIncludeWithIterate_WhenExecuted_ThenOnlyResourcesReachedFromTheRankedPageAreIncluded()
        {
            // Arrange
            using SqlConnection connection = await OpenSeededConnectionAsync();
            var include = new IncludeExpression(["Observation"], SubjectParameter, "Observation", "Patient", null, false, false, false);

            // Iterate includes are scoped to their source type, as SearchOptionsFactory builds them.
            var iterate = new IncludeExpression(["Patient"], LinkParameter, "Patient", "Patient", null, false, false, true);
            using SqlCommand command = CreateSearchCommand(connection, [include, iterate], CreateOptions(maxItemCount: 2));

            // Act
            List<SearchRow> rows = await ReadRowsAsync(command);

            // Assert - patient-near's link is followed; the lookahead match's patient-far, and its link, are not.
            Assert.Equal(["obs-near", "obs-mid", "obs-far"], rows.Where(r => r.IsMatch).Select(r => r.Id));
            AssertRankedDistances(rows);
            Assert.Equal(["patient-mid", "patient-near", "patient-unrelated"], rows.Where(r => !r.IsMatch).Select(r => r.Id).Order());
            Assert.All(rows.Where(r => !r.IsMatch), r => Assert.Null(r.Distance));
        }

        [Fact]
        public async Task GivenSemanticCountOnly_WhenExecuted_ThenDistinctMatchingResourcesAreCounted()
        {
            // Arrange
            using SqlConnection connection = await OpenSeededConnectionAsync();
            using SqlCommand command = CreateSearchCommand(connection, include: null, CreateOptions(maxItemCount: 10, countOnly: true));

            // Act
            object count = await command.ExecuteScalarAsync();

            // Assert - two matching chunks of one resource count once; deleted, unrelated and unembedded resources do not count.
            Assert.Equal(3L, Convert.ToInt64(count, CultureInfo.InvariantCulture));
        }

        // Matches carry ascending distances; included resources are not semantic matches and carry none.
        private static void AssertRankedDistances(List<SearchRow> rows)
        {
            double[] distances = rows.Where(r => r.IsMatch).Select(r => Assert.NotNull(r.Distance)).ToArray();
            Assert.Equal(distances.Order(), distances);
            Assert.InRange(distances[0], 0, 0.001);
        }

        private async Task<SqlConnection> OpenSeededConnectionAsync()
        {
            var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            return connection;
        }

        private static SqlSearchOptions CreateOptions(
            int maxItemCount,
            bool countOnly = false,
            IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> sort = null)
        {
            return new SqlSearchOptions(new SearchOptions
            {
                MaxItemCount = maxItemCount,

                // SearchOptionsFactory always sets this (default 1000); left at 0 the include CTEs return only one resource.
                IncludeCount = 10,
                CountOnly = countOnly,
                SearchParameters = [],
                UnsupportedSearchParams = [],
                Sort = sort ?? [],
                ResourceVersionTypes = ResourceVersionType.Latest,
            })
            {
                PreparedVectorQuery = new PreparedVectorSearchQuery(
                    SemanticParameter,
                    EmbeddingModelId,
                    Enumerable.Repeat(0.25f, VectorSearchConfiguration.SupportedDimensions).ToArray(),
                    minimumScore: 0.65m),
            };
        }

        // Runs the production rewriter sequence for Observation?<semantic>[&_include|_revinclude] and redirects tables to the local fixture.
        private SqlCommand CreateSearchCommand(SqlConnection connection, IncludeExpression include, SqlSearchOptions options) =>
            CreateSearchCommand(connection, include == null ? [] : [include], options);

        private SqlCommand CreateSearchCommand(SqlConnection connection, IReadOnlyList<IncludeExpression> includes, SqlSearchOptions options)
        {
            var model = Substitute.For<ISqlServerFhirModel>();
            (string Name, short Id)[] types = [("Observation", ObservationTypeId), ("Patient", PatientTypeId), ("DiagnosticReport", DiagnosticReportTypeId)];
            foreach ((string name, short id) in types)
            {
                model.GetResourceTypeId(name).Returns(id);
                model.TryGetResourceTypeId(name, out Arg.Any<short>()).Returns(call =>
                {
                    call[1] = id;
                    return true;
                });
            }

            model.GetSearchParamId(SemanticParameter.Url).Returns(SemanticSearchParamId);
            model.GetSearchParamId(SubjectParameter.Url).Returns((short)10);
            model.GetSearchParamId(ResultParameter.Url).Returns((short)11);
            model.GetSearchParamId(HasMemberParameter.Url).Returns((short)12);
            model.GetSearchParamId(LinkParameter.Url).Returns((short)13);

            Expression typePredicate = Expression.SearchParameter(
                SearchParameterInfo.ResourceTypeSearchParameter,
                Expression.StringEquals(FieldName.TokenCode, null, "Observation", false));
            Expression expression = Expression.And([typePredicate, .. includes]);

            var factory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var root = (SqlRootExpression)expression
                .AcceptVisitor(UntypedReferenceRewriter.Instance)
                .AcceptVisitor(new SqlRootExpressionRewriter(factory))
                .AcceptVisitor(IncludeMatchSeedRewriter.Instance)
                .AcceptVisitor(TopRewriter.Instance, options)
                .AcceptVisitor(IncludeRewriter.Instance);
            SqlCommand command = connection.CreateCommand();
            var builder = new IndentedStringBuilder(new StringBuilder());
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var generator = new SqlQueryGenerator(
                builder,
                new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                model,
                schema,
                factory,
                false,
                false);
            generator.VisitSqlRoot(root, options);
            SqlCommandSimplifier.RemoveRedundantParameters(builder, command.Parameters, NullLogger.Instance);
            command.CommandText = builder.ToString()
                .Replace("dbo.VectorSearchParam", "#VectorSearchParam", StringComparison.Ordinal)
                .Replace("dbo.ReferenceSearchParam", "#ReferenceSearchParam", StringComparison.Ordinal)
                .Replace("dbo.Resource", "#Resource", StringComparison.Ordinal);
            _output.WriteLine(command.CommandText);
            return command;
        }

        // Executes the search and preserves every returned row, including duplicates, for result assertions.
        private static async Task<List<SearchRow>> ReadRowsAsync(SqlCommand command)
        {
            using SqlDataReader reader = await command.ExecuteReaderAsync();
            var rows = new List<SearchRow>();
            int idOrdinal = reader.GetOrdinal("ResourceId");
            int matchOrdinal = reader.GetOrdinal("IsMatch");
            int distanceOrdinal = reader.GetOrdinal("SemanticDistance");
            while (await reader.ReadAsync())
            {
                rows.Add(new SearchRow(
                    reader.GetString(idOrdinal),
                    reader.GetBoolean(matchOrdinal),
                    reader.IsDBNull(distanceOrdinal) ? null : Convert.ToDouble(reader.GetValue(distanceOrdinal), CultureInfo.InvariantCulture)));
            }

            return rows;
        }

        // Configures SQL workload identity authentication when Azure Pipelines credentials are available.
        private static void ConfigureWorkloadIdentityAuthentication()
        {
            string clientId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionClientId);
            string tenantId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionTenantId);
            string serviceConnectionId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId);
            string systemAccessToken = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SystemAccessToken);

            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(serviceConnectionId) && !string.IsNullOrEmpty(systemAccessToken))
            {
                var credential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
                SqlAuthenticationProvider.SetProvider(
                    SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity,
                    new SqlAzurePipelinesWorkloadIdentityAuthenticationProvider(credential));
            }
        }

        // Creates a test search parameter with a stable URL and optional reference target types.
        private static SearchParameterInfo Parameter(string name, SearchParamType type, string[] targets = null) =>
            new(name, name, type, new Uri($"http://example.org/SearchParameter/{name}"), null, name, targets);

        // Builds a vector whose cosine distance to the uniform query vector is 1 - sqrt(ones / dimensions).
        private static string LeadingOnes(int ones) =>
            "[" + string.Join(",", Enumerable.Range(0, VectorSearchConfiguration.SupportedDimensions).Select(i => i < ones ? "1" : "0")) + "]";

        // Builds a vector orthogonal to the uniform query vector (cosine distance 1, beyond the 0.65 score threshold).
        private static string Alternating() =>
            "[" + string.Join(",", Enumerable.Range(0, VectorSearchConfiguration.SupportedDimensions).Select(i => i % 2 == 0 ? "1" : "-1")) + "]";

        // Seeds connection-local tables. Relevance order (near, mid, far) is the reverse of surrogate order.
        private static async Task SeedAsync(SqlConnection connection)
        {
            using (SqlCommand create = connection.CreateCommand())
            {
                // Temp tables are created in an unparameterized session batch so subsequent commands can access them.
                create.CommandText = $"""
                    CREATE TABLE #Resource (
                        ResourceTypeId smallint, ResourceSurrogateId bigint, ResourceId varchar(64),
                        IsHistory bit, IsDeleted bit, Version int DEFAULT 1, RequestMethod varchar(10),
                        IsRawResourceMetaSet bit DEFAULT 0, SearchParamHash varchar(64), RawResource varbinary(max) DEFAULT 0x01);
                    CREATE INDEX IX_Resource_ResourceTypeId_ResourceSurrgateId ON #Resource (ResourceTypeId, ResourceSurrogateId);
                    CREATE TABLE #ReferenceSearchParam (
                        ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                        ReferenceResourceTypeId smallint NULL, ReferenceResourceId varchar(64), IsHistory bit);
                    CREATE TABLE #VectorSearchParam (
                        ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint, ChunkOrdinal smallint,
                        EmbeddingModelId smallint, Embedding vector({VectorSearchConfiguration.SupportedDimensions}));
                    """;
                await create.ExecuteNonQueryAsync();
            }

            using SqlCommand seed = connection.CreateCommand();
            seed.CommandText = $"""
                INSERT INTO #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted) VALUES
                    ({ObservationTypeId}, 100, 'obs-far', 0, 0), ({ObservationTypeId}, 150, 'obs-mid', 0, 0),
                    ({ObservationTypeId}, 200, 'obs-near', 0, 0), ({ObservationTypeId}, 260, 'obs-deleted', 0, 1),
                    ({ObservationTypeId}, 300, 'obs-unrelated', 0, 0), ({ObservationTypeId}, 50, 'obs-unembedded', 0, 0),
                    ({PatientTypeId}, 1100, 'patient-far', 0, 0), ({PatientTypeId}, 1150, 'patient-mid', 0, 0),
                    ({PatientTypeId}, 1200, 'patient-near', 0, 0), ({PatientTypeId}, 1300, 'patient-unrelated', 0, 0),
                    ({PatientTypeId}, 1050, 'patient-unembedded', 0, 0), ({DiagnosticReportTypeId}, 2000, 'report-near', 0, 0);
                INSERT INTO #ReferenceSearchParam VALUES
                    ({ObservationTypeId}, 100, 10, {PatientTypeId}, 'patient-far', 0), ({ObservationTypeId}, 150, 10, {PatientTypeId}, 'patient-mid', 0),
                    ({ObservationTypeId}, 200, 10, {PatientTypeId}, 'patient-near', 0), ({ObservationTypeId}, 300, 10, {PatientTypeId}, 'patient-unrelated', 0),
                    ({ObservationTypeId}, 50, 10, {PatientTypeId}, 'patient-unembedded', 0),
                    ({DiagnosticReportTypeId}, 2000, 11, {ObservationTypeId}, 'obs-near', 0),
                    ({ObservationTypeId}, 150, 12, {ObservationTypeId}, 'obs-near', 0),
                    ({PatientTypeId}, 1200, 13, {PatientTypeId}, 'patient-unrelated', 0),
                    ({PatientTypeId}, 1100, 13, {PatientTypeId}, 'patient-unembedded', 0);
                INSERT INTO #VectorSearchParam VALUES
                    ({ObservationTypeId}, 200, {SemanticSearchParamId}, 0, {EmbeddingModelId}, CAST(@uniform AS vector({VectorSearchConfiguration.SupportedDimensions}))),
                    ({ObservationTypeId}, 200, {SemanticSearchParamId}, 1, {EmbeddingModelId}, CAST(@half AS vector({VectorSearchConfiguration.SupportedDimensions}))),
                    ({ObservationTypeId}, 150, {SemanticSearchParamId}, 0, {EmbeddingModelId}, CAST(@half AS vector({VectorSearchConfiguration.SupportedDimensions}))),
                    ({ObservationTypeId}, 100, {SemanticSearchParamId}, 0, {EmbeddingModelId}, CAST(@quarter AS vector({VectorSearchConfiguration.SupportedDimensions}))),
                    ({ObservationTypeId}, 260, {SemanticSearchParamId}, 0, {EmbeddingModelId}, CAST(@uniform AS vector({VectorSearchConfiguration.SupportedDimensions}))),
                    ({ObservationTypeId}, 300, {SemanticSearchParamId}, 0, {EmbeddingModelId}, CAST(@alternating AS vector({VectorSearchConfiguration.SupportedDimensions})));
                """;
            seed.Parameters.AddWithValue("@uniform", LeadingOnes(VectorSearchConfiguration.SupportedDimensions));
            seed.Parameters.AddWithValue("@half", LeadingOnes(VectorSearchConfiguration.SupportedDimensions / 2));
            seed.Parameters.AddWithValue("@quarter", LeadingOnes(VectorSearchConfiguration.SupportedDimensions / 4));
            seed.Parameters.AddWithValue("@alternating", Alternating());
            await seed.ExecuteNonQueryAsync();
        }

        private sealed record SearchRow(string Id, bool IsMatch, double? Distance);
    }
}
