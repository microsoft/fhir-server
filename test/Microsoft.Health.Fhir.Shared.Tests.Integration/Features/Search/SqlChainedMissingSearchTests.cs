// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
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
    /// Checks linked-resource absence using generated SQL and connection-local graph witnesses.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait("DataStore", "SqlServer")]
    public class SqlChainedMissingSearchTests
    {
        private static readonly SearchParameterInfo Birthdate = Parameter("birthdate", SearchParamType.Date);
        private static readonly SearchParameterInfo Subject = Parameter("subject", SearchParamType.Reference, ["Patient"]);
        private static readonly SearchParameterInfo PatientReference = Parameter("patient", SearchParamType.Reference, ["Patient"]);
        private static readonly SearchParameterInfo Gender = Parameter("gender", SearchParamType.Token);

        static SqlChainedMissingSearchTests()
        {
            SqlAzurePipelinesWorkloadIdentityAuthenticationProvider.ConfigureFromEnvironment();
        }

        /// <summary>
        /// Preserves target identity, exact result sets, duplicate suppression, and counts.
        /// </summary>
        /// <param name="shape">The graph predicate to execute.</param>
        /// <param name="expected">The matching resource identifiers.</param>
        /// <returns>The test task.</returns>
        [Theory]
        [InlineData("direct-missing", "missing", "missing-female")]
        [InlineData("forward-missing", "observation-missing", "observation-missing-female")]
        [InlineData("reverse-missing", "day", "missing")]
        [InlineData("reverse-present", "year", "day")]
        [InlineData("nested-missing", "observation-day", "observation-missing")]
        [InlineData("reverse-missing-with-birthdate", "day")]
        [InlineData("forward-missing-and-gender", "observation-missing")]
        [InlineData("reverse-missing-and-gender", "missing")]
        [InlineData("reverse-forward-missing", "missing")]
        [InlineData("sibling-missing-and-present", "day")]
        [InlineData("reverse-missing-gender", "year")]
        [InlineData("scope-birthdate-token-reverse-missing", "day")]
        public async Task GivenMissingParametersInPatientGraph_WhenSearched_ThenMissingAppliesToTheCorrectResource(
            string shape,
            params string[] expected)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            using var command = CreateCommand(connection, shape);

            var actual = await ReadIdsAsync(command);

            Assert.Equal(expected.Order(), actual.Order());
            Assert.Equal(actual.Count, actual.Distinct().Count());
            using var count = CreateCommand(connection, shape, countOnly: true);
            Assert.Equal(expected.Length, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }

        /// <summary>
        /// Pins the target-correlated absence predicate without opening a database connection.
        /// </summary>
        /// <param name="shape">The graph predicate to generate.</param>
        [Theory]
        [InlineData("forward-missing")]
        [InlineData("reverse-missing")]
        [InlineData("nested-missing")]
        [InlineData("reverse-missing-with-birthdate")]
        [InlineData("forward-missing-and-gender")]
        [InlineData("reverse-missing-and-gender")]
        [InlineData("reverse-forward-missing")]
        [InlineData("sibling-missing-and-present")]
        [InlineData("reverse-missing-gender")]
        [InlineData("scope-birthdate-token-reverse-missing")]
        public void GivenChainedMissing_WhenSqlGenerated_ThenAbsenceIsCorrelatedToALiveTarget(string shape)
        {
            using var connection = new SqlConnection();

            using var command = CreateCommand(connection, shape);

            Assert.Contains("NOT EXISTS (", command.CommandText);
            Assert.Matches(@"missingIndex.IndexedResourceTypeId = ResourceTypeId\s", command.CommandText);
            Assert.Matches(@"missingIndex.IndexedResourceSurrogateId = ResourceSurrogateId\s", command.CommandText);
            Assert.Contains("missingIndex.SearchParamId =", command.CommandText);
            Assert.Contains("IsHistory = 0", command.CommandText);
            Assert.Contains("IsDeleted = 0", command.CommandText);
            Assert.Contains(shape == "reverse-missing-gender" ? "#TokenSearchParam) missingIndex" : "#DateTimeSearchParam) missingIndex", command.CommandText);
        }

        private static SearchParameterInfo Parameter(string name, SearchParamType type, string[] targets = null) =>
            new(name, name, type, new Uri($"http://example.org/SearchParameter/{name}"), null, name, targets);

        private static Expression Equality(string date)
        {
            var range = DateTimeSearchValue.Parse(date);
            return Expression.SearchParameter(Birthdate, Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, range.Start),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, range.End)));
        }

        private static Expression Scope(SearchParameterInfo typeParameter, params Expression[] parameters)
        {
            var predicates = parameters.Select(parameter =>
            {
                var predicate = Expression.And(
                    Expression.SearchParameter(typeParameter, Expression.StringEquals(FieldName.TokenCode, null, "Patient", false)),
                    parameter);
                predicate.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                return predicate;
            });
            var scope = Expression.And(predicates.ToArray());
            scope.IsSmartV2UnionExpressionForScopesSearchParameters = true;
            return scope;
        }

        private static SqlCommand CreateCommand(SqlConnection connection, string shape, bool countOnly = false)
        {
            var model = Substitute.For<ISqlServerFhirModel>();
            string[] resourceTypes = ["Patient", "Observation", "RelatedPerson"];
            model.ResourceTypeIdRange.Returns(((short)1, (short)3));
            for (short i = 0; i < resourceTypes.Length; i++)
            {
                short id = (short)(i + 1);
                string type = resourceTypes[i];
                model.GetResourceTypeId(type).Returns(id);
                model.GetResourceTypeName(id).Returns(type);
                model.TryGetResourceTypeId(type, out Arg.Any<short>()).Returns(call =>
                {
                    call[1] = id;
                    return true;
                });
            }

            model.GetSearchParamId(Birthdate.Url).Returns((short)10);
            model.GetSearchParamId(Subject.Url).Returns((short)11);
            model.GetSearchParamId(PatientReference.Url).Returns((short)13);
            model.GetSearchParamId(Gender.Url).Returns((short)14);
            var typeParameter = new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType);
            var definitions = Substitute.For<ISearchParameterDefinitionManager>();
            definitions.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType).Returns(typeParameter);
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var missingBirthdate = Expression.MissingSearchParameter(Birthdate, true);
            var reverseMissing = Expression.Chained(["RelatedPerson"], PatientReference, ["Patient"], true, missingBirthdate);
            var reversePresent = Expression.Chained(
                ["RelatedPerson"], PatientReference, ["Patient"], true, Expression.MissingSearchParameter(Birthdate, false));
            var male = Expression.SearchParameter(Gender, Expression.StringEquals(FieldName.TokenCode, null, "male", false));
            string resourceType = shape is "forward-missing" or "forward-missing-and-gender" or "nested-missing" ? "Observation" : "Patient";
            Expression expression = shape switch
            {
                "direct-missing" => missingBirthdate,
                "forward-missing" => Expression.Chained(["Observation"], Subject, ["Patient"], false, missingBirthdate),
                "reverse-missing" => reverseMissing,
                "reverse-present" => reversePresent,
                "nested-missing" => Expression.Chained(["Observation"], Subject, ["Patient"], false, reverseMissing),
                "reverse-missing-with-birthdate" or "scope-birthdate-token-reverse-missing" => Expression.And(Equality("2016-07-06"), reverseMissing),
                "forward-missing-and-gender" => Expression.Chained(
                    ["Observation"], Subject, ["Patient"], false, Expression.And(missingBirthdate, male)),
                "reverse-missing-and-gender" => Expression.Chained(
                    ["RelatedPerson"], PatientReference, ["Patient"], true, Expression.And(missingBirthdate, male)),
                "reverse-forward-missing" => Expression.Chained(
                    ["RelatedPerson"],
                    PatientReference,
                    ["Patient"],
                    true,
                    Expression.Chained(["RelatedPerson"], PatientReference, ["Patient"], false, missingBirthdate)),
                "sibling-missing-and-present" => Expression.And(reverseMissing, reversePresent),
                "reverse-missing-gender" => Expression.Chained(
                    ["RelatedPerson"], PatientReference, ["Patient"], true, Expression.MissingSearchParameter(Gender, true)),
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            };
            if (shape == "scope-birthdate-token-reverse-missing")
            {
                var union = Expression.Union(
                    UnionOperator.All,
                    [
                        Scope(typeParameter, Equality("2016-07-06"), male),
                        Scope(typeParameter, Equality("2016-12-31"), Expression.SearchParameter(Gender, Expression.StringEquals(FieldName.TokenCode, null, "female", false))),
                        Scope(typeParameter, Equality("2016-12-31"), male),
                    ]);
                union.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                expression = Expression.And(union, expression);
            }

            expression = Expression.And(
                Expression.SearchParameter(typeParameter, Expression.StringEquals(FieldName.TokenCode, null, resourceType, false)),
                expression);
            var factory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var options = new SqlSearchOptions(new SearchOptions
            {
                SearchParameters = [],
                UnsupportedSearchParams = [],
                Sort = [(new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Ascending)],
                MaxItemCount = 100,
                ResourceVersionTypes = ResourceVersionType.Latest,
                CountOnly = countOnly,
            });
            var root = (SqlRootExpression)expression
                .AcceptVisitor(FlatteningRewriter.Instance)
                .AcceptVisitor(UntypedReferenceRewriter.Instance)
                .AcceptVisitor(new SqlRootExpressionRewriter(factory))
                .AcceptVisitor(DateTimeTableExpressionCombiner.Instance)
                .AcceptVisitor(new PartitionEliminationRewriter(model, schema, () => definitions))
                .AcceptVisitor(new SortRewriter(factory), options)
                .AcceptVisitor(SearchParamTableExpressionReorderer.Instance)
                .AcceptVisitor(MissingSearchParamVisitor.Instance)
                .AcceptVisitor(NotExpressionRewriter.Instance)
                .AcceptVisitor(new ChainFlatteningRewriter(factory))
                .AcceptVisitor(ResourceColumnPredicatePushdownRewriter.Instance)
                .AcceptVisitor(DateTimeBoundedRangeRewriter.Instance)
                .AcceptVisitor(IncludeMatchSeedRewriter.Instance)
                .AcceptVisitor(TopRewriter.Instance, options)
                .AcceptVisitor(IncludeRewriter.Instance);
            var command = connection.CreateCommand();
            var builder = new IndentedStringBuilder(new StringBuilder());
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
                .Replace("dbo.ReferenceSearchParam", "#ReferenceSearchParam", StringComparison.Ordinal)
                .Replace("dbo.DateTimeSearchParam", "#DateTimeSearchParam", StringComparison.Ordinal)
                .Replace("dbo.TokenSearchParam", "#TokenSearchParam", StringComparison.Ordinal)
                .Replace("dbo.Resource", "#Resource", StringComparison.Ordinal);
            return command;
        }

        private static async Task<List<string>> ReadIdsAsync(SqlCommand command)
        {
            using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(reader.GetOrdinal("ResourceId")));
            }

            return rows;
        }

        private static async Task SeedAsync(SqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE #Resource (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, ResourceId varchar(64),
                    IsHistory bit, IsDeleted bit, Version int DEFAULT 1, RequestMethod varchar(10),
                    IsRawResourceMetaSet bit, SearchParamHash varchar(64), RawResource varbinary(max));
                CREATE TABLE #DateTimeSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    StartDateTime datetime2(7), EndDateTime datetime2(7), IsLongerThanADay bit,
                    IsMin bit DEFAULT 1, IsMax bit DEFAULT 1);
                CREATE TABLE #ReferenceSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    ReferenceResourceTypeId smallint, ReferenceResourceId varchar(64), BaseUri varchar(128));
                CREATE TABLE #TokenSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    Code varchar(256), SystemId int, CodeOverflow varchar(max));
                """;
            await command.ExecuteNonQueryAsync();
            (string Id, string Date)[] dates =
            [
                ("year", "2016"), ("month", "2016-07"), ("day", "2016-07-06"),
                ("month-end", "2016-07-31"), ("december", "2016-12"), ("next-day", "2016-07-07"),
            ];
            for (int i = 0; i < dates.Length; i++)
            {
                var range = DateTimeSearchValue.Parse(dates[i].Date);
                command.Parameters.Clear();
                command.CommandText = """
                    INSERT #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                        VALUES (1, @sid, @id, 0, 0), (2, @sid + 100, CONCAT('observation-', @id), 0, 0);
                    INSERT #DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay)
                        VALUES (1, @sid, 10, @start, @end, @long);
                    INSERT #ReferenceSearchParam VALUES (2, @sid + 100, 11, 1, @id, NULL);
                    """;
                command.Parameters.AddWithValue("@sid", (long)(i + 1));
                command.Parameters.AddWithValue("@id", dates[i].Id);
                command.Parameters.AddWithValue("@start", range.Start.UtcDateTime);
                command.Parameters.AddWithValue("@end", range.End.UtcDateTime);
                command.Parameters.AddWithValue("@long", range.End - range.Start > TimeSpan.FromDays(1));
                command.Parameters["@start"].SqlDbType = System.Data.SqlDbType.DateTime2;
                command.Parameters["@end"].SqlDbType = System.Data.SqlDbType.DateTime2;
                await command.ExecuteNonQueryAsync();
            }

            command.Parameters.Clear();
            command.CommandText = """
                INSERT #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                    VALUES (1, 20, 'missing', 0, 0), (2, 120, 'observation-missing', 0, 0),
                           (1, 21, 'missing-female', 0, 0), (2, 123, 'observation-missing-female', 0, 0),
                           (1, 90, 'year', 1, 0),
                           (2, 121, 'observation-dangling', 0, 0), (2, 122, 'observation-wrong-type', 0, 0),
                           (3, 201, 'related-missing', 0, 0), (3, 202, 'related-present', 0, 0),
                           (3, 203, 'related-mixed-missing', 0, 0), (3, 204, 'related-mixed-present', 0, 0),
                           (3, 205, 'related-no-reference', 0, 0),
                           (3, 206, 'related-deleted', 0, 1), (3, 207, 'related-history', 1, 0),
                           (3, 208, 'related-dangling', 0, 0), (3, 209, 'related-second-missing', 0, 0);
                INSERT #ReferenceSearchParam VALUES
                    (2, 120, 11, 1, 'missing', NULL), (2, 123, 11, 1, 'missing-female', NULL),
                    (2, 121, 11, 1, 'does-not-exist', NULL), (2, 122, 11, 3, 'related-missing', NULL),
                    (3, 201, 13, 1, 'missing', NULL), (3, 202, 13, 1, 'year', NULL),
                    (3, 203, 13, 1, 'day', NULL), (3, 204, 13, 1, 'day', NULL),
                    (3, 206, 13, 1, 'year', NULL), (3, 207, 13, 1, 'year', NULL),
                    (3, 208, 13, 1, 'does-not-exist', NULL), (3, 209, 13, 1, 'missing', NULL);
                INSERT #DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay)
                    SELECT 3, related.Sid, 10, dates.StartDateTime, dates.EndDateTime, dates.IsLongerThanADay
                    FROM #DateTimeSearchParam dates CROSS JOIN (VALUES (202), (204)) related(Sid)
                    WHERE dates.ResourceTypeId = 1 AND dates.ResourceSurrogateId = 3;
                INSERT #DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay)
                    SELECT target.ResourceTypeId, target.Sid, 99, dates.StartDateTime, dates.EndDateTime, dates.IsLongerThanADay
                    FROM #DateTimeSearchParam dates CROSS JOIN (VALUES (1, 20), (3, 201)) target(ResourceTypeId, Sid)
                    WHERE dates.ResourceTypeId = 1 AND dates.ResourceSurrogateId = 3;
                INSERT #TokenSearchParam
                    SELECT 1, ResourceSurrogateId, 14,
                        CASE WHEN ResourceId IN ('month', 'december', 'missing-female') THEN 'female' ELSE 'male' END, NULL, NULL
                    FROM #Resource WHERE ResourceTypeId = 1;
                INSERT #TokenSearchParam VALUES
                    (3, 201, 14, 'male', NULL, NULL),
                    (3, 203, 14, 'female', NULL, NULL), (3, 204, 14, 'male', NULL, NULL),
                    (3, 209, 14, 'male', NULL, NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
