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
using Microsoft.Health.Fhir.Tests.Integration.Features.Search;
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
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait("DataStore", "SqlServer")]
    public class SqlCalendarDateSearchTests
    {
        private static readonly ISearchParameterDefinitionManager DateDefinitions = new SqlInstantDateTimeSearchTests.InstantSearchFixture().Definitions;
        private static readonly SearchParameterInfo Birthdate = DateDefinitions.GetSearchParameter("Patient", "birthdate");
        private static readonly SearchParameterInfo PaymentDate = DateDefinitions.GetSearchParameter("ClaimResponse", "payment-date");

        private static readonly SearchParameterInfo Subject = new(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            expression: "Observation.subject",
            targetResourceTypes: ["Patient"]);

        private static readonly SearchParameterInfo Code = new(
            "code", "code", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/clinical-code"));

        private static readonly SearchParameterInfo Gender = new(
            "gender",
            "gender",
            SearchParamType.Token,
            new Uri("http://hl7.org/fhir/SearchParameter/individual-gender"),
            expression: "Patient.gender",
            baseResourceTypes: ["Patient"]);

        private static readonly SearchParameterInfo Organization = new(
            "organization",
            "organization",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
            expression: "Patient.managingOrganization",
            targetResourceTypes: ["Organization"]);

        private static readonly SearchParameterInfo OrganizationIdentifier = new(
            "identifier", "identifier", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Organization-identifier"));

        private readonly ITestOutputHelper _output;

        static SqlCalendarDateSearchTests()
        {
            SqlAzurePipelinesWorkloadIdentityAuthenticationProvider.ConfigureFromEnvironment();
        }

        public SqlCalendarDateSearchTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("Patient", "birthdate", false)]
        [InlineData("Patient", "birthdate", true)]
        [InlineData("ClaimResponse", "payment-date", false)]
        [InlineData("ClaimResponse", "payment-date", true)]
        public async Task GivenCalendarDateEquality_WhenCountedAndPaged_ThenSelectedSemanticsArePreserved(
            string resourceType,
            string code,
            bool containment)
        {
            Assert.Equal(TemporalType.Date, DateDefinitions.GetSearchParameter(resourceType, code).TemporalType);
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            using var legacy = CreateCommand(connection, "direct", sorted: false, scalar: false, rootResourceType: resourceType, searchCode: code, containment: containment);
            using var optimized = CreateCommand(connection, "direct", sorted: false, scalar: true, rootResourceType: resourceType, searchCode: code, containment: containment);

            var expected = await ReadRowsAsync(legacy);
            var actual = await ReadRowsAsync(optimized);

            Assert.Equal((containment ? new[] { "day" } : new[] { "year", "month", "day" }).Order(), actual.Select(row => row.Id).Order());
            Assert.Equal(expected, actual);
            Assert.Matches(@"EndDateTime\s*=\s*@\w+", optimized.CommandText);
            foreach (bool scalar in new[] { false, true })
            {
                using var count = CreateCommand(connection, "direct", sorted: false, scalar: scalar, countOnly: true, rootResourceType: resourceType, searchCode: code, containment: containment);
                Assert.Equal(expected.Count, Convert.ToInt32(await count.ExecuteScalarAsync()));
                var paged = new List<(string Id, bool IsMatch, long Sid)>();
                long? afterSid = null;
                for (int page = 0; page <= expected.Count; page++)
                {
                    using var command = CreateCommand(connection, "direct", sorted: false, scalar: scalar, afterSid: afterSid, pageSize: 1, rootResourceType: resourceType, searchCode: code, containment: containment);
                    var rows = await ReadRowsAsync(command);
                    Assert.InRange(rows.Count, 0, 2);
                    if (rows.Count == 0)
                    {
                        break;
                    }

                    paged.Add(rows[0]);
                    afterSid = rows[0].Sid;
                }

                Assert.Equal(expected, paged);
                Assert.Equal(paged.Count, paged.Distinct().Count());
            }
        }

        [Theory]
        [InlineData("direct", false)]
        [InlineData("forward", false)]
        [InlineData("reverse", false)]
        [InlineData("direct", true)]
        [InlineData("reverse", true)]
        [InlineData("include", false)]
        [InlineData("revinclude", false)]
        [InlineData("or", false)]
        [InlineData("repeated", false)]
        [InlineData("scope", false)]
        [InlineData("scope-revinclude", false)]
        public async Task GivenExactDayPatientBirthdate_WhenComposedWithOtherSearches_ThenLegacyResultsArePreserved(string shape, bool sorted)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            if (shape == "scope-revinclude")
            {
                using var canary = connection.CreateCommand();
                canary.CommandText = """
                    INSERT #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                        VALUES (2, 1000, 'outside-scope', 0, 0);
                    INSERT #ReferenceSearchParam VALUES (2, 1000, 11, 1, 'year', NULL);
                    """;
                await canary.ExecuteNonQueryAsync();
            }

            using var legacy = CreateCommand(connection, shape, sorted, scalar: false);
            using var optimized = CreateCommand(connection, shape, sorted, scalar: true);
            _output.WriteLine(legacy.CommandText);
            _output.WriteLine(optimized.CommandText);

            var expected = await ReadRowsAsync(legacy);
            var actual = await ReadRowsAsync(optimized);

            Assert.Equal(expected, actual);
            Assert.Equal(actual.Count, actual.Distinct().Count());
            if (shape != "or")
            {
                Assert.Matches(@"EndDateTime\s*=\s*@\w+", optimized.CommandText);
            }

            string prefix = shape is "forward" or "include" ? "observation-" : string.Empty;
            string[] expectedMatches = shape switch
            {
                "repeated" => ["year", "month"],
                "or" => ["year", "month", "day", "month-end"],
                "scope" or "scope-revinclude" => ["year", "day"],
                _ => [prefix + "year", prefix + "month", prefix + "day"],
            };
            Assert.Equal(expectedMatches.Order(), actual.Where(r => r.IsMatch).Select(r => r.Id).Order());
            if (shape is "include" or "revinclude" or "scope-revinclude")
            {
                string includePrefix = shape == "include" ? string.Empty : "observation-";
                Assert.Equal(
                    (shape == "scope-revinclude"
                        ? new[] { includePrefix + "year", includePrefix + "day" }
                        : new[] { includePrefix + "year", includePrefix + "month", includePrefix + "day" }).Order(),
                    actual.Where(r => !r.IsMatch).Select(r => r.Id).Order());
            }
        }

        [Theory]
        [InlineData("direct")]
        [InlineData("forward")]
        [InlineData("reverse")]
        public async Task GivenExactDayPatientBirthdate_WhenCounted_ThenPartialDatesRemainMatches(string shape)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            using var command = CreateCommand(connection, shape, sorted: false, scalar: true, countOnly: true);

            var count = Convert.ToInt32(await command.ExecuteScalarAsync());

            Assert.Equal(3, count);
        }

        [Theory]
        [InlineData("Patient", "birthdate")]
        [InlineData("ClaimResponse", "payment-date")]
        public async Task GivenCalendarDateOverlapSemantics_WhenScalarOptimizationEnabled_ThenSqlContainsAnEndEqualityFastPath(string resourceType, string code)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            using var command = CreateCommand(connection, "direct", sorted: false, scalar: true, rootResourceType: resourceType, searchCode: code);

            var rows = await ReadRowsAsync(command);

            Assert.Equal(3, rows.Count);
            Assert.Matches(@"EndDateTime\s*=\s*@\w+", command.CommandText);
            Assert.DoesNotContain("UNION ALL", command.CommandText, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Patient", "birthdate", "2016", "year", "month", "day", "month-end", "december", "next-day")]
        [InlineData("Patient", "birthdate", "2016-07", "year", "month", "day", "month-end", "next-day")]
        [InlineData("Patient", "birthdate", "2016-07-06T12:00:00Z", "year", "month", "day")]
        [InlineData("Patient", "birthdate", "2016-07-31", "year", "month", "month-end")]
        [InlineData("Patient", "birthdate", "2016-12-31", "year", "december")]
        [InlineData("Patient", "birthdate", "2000-02-29", "leap-year", "leap-month", "leap-day")]
        [InlineData("Patient", "birthdate", "0001-01-01", "min-year", "min-month", "min-day")]
        [InlineData("Patient", "birthdate", "9999-12-31", "max-year", "max-month", "max-day")]
        [InlineData("ClaimResponse", "payment-date", "2016", "year", "month", "day", "month-end", "december", "next-day")]
        [InlineData("ClaimResponse", "payment-date", "2016-07", "year", "month", "day", "month-end", "next-day")]
        [InlineData("ClaimResponse", "payment-date", "2016-07-06T12:00:00Z", "year", "month", "day")]
        [InlineData("ClaimResponse", "payment-date", "2016-07-31", "year", "month", "month-end")]
        [InlineData("ClaimResponse", "payment-date", "2016-12-31", "year", "december")]
        [InlineData("ClaimResponse", "payment-date", "2000-02-29", "leap-year", "leap-month", "leap-day")]
        [InlineData("ClaimResponse", "payment-date", "0001-01-01", "min-year", "min-month", "min-day")]
        [InlineData("ClaimResponse", "payment-date", "9999-12-31", "max-year", "max-month", "max-day")]
        public async Task GivenQueryPrecisionAndCalendarBoundaries_WhenOptimized_ThenOverlapResultsAreUnchanged(
            string resourceType,
            string code,
            string query,
            params string[] expected)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            using var legacy = CreateCommand(connection, "direct", sorted: false, scalar: false, query: query, rootResourceType: resourceType, searchCode: code);
            using var optimized = CreateCommand(connection, "direct", sorted: false, scalar: true, query: query, rootResourceType: resourceType, searchCode: code);

            var originalRows = await ReadRowsAsync(legacy);
            var actualRows = await ReadRowsAsync(optimized);

            Assert.Equal(expected.Order(), actualRows.Select(r => r.Id).Order());
            Assert.Equal(originalRows, actualRows);
            if (query.Length != 10)
            {
                Assert.Equal(legacy.CommandText, optimized.CommandText);
            }
        }

        [Theory]
        [InlineData("direct")]
        [InlineData("forward")]
        [InlineData("reverse")]
        public async Task GivenSmallPatientGraphKeysetPages_WhenOptimized_ThenEveryLegacyMatchIsReturnedOnce(string shape)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            using var legacy = CreateCommand(connection, shape, sorted: false, scalar: false);
            var expected = await ReadRowsAsync(legacy);
            var actual = new List<(string Id, bool IsMatch, long Sid)>();
            long? afterSid = null;

            for (int page = 0; page <= expected.Count; page++)
            {
                using var command = CreateCommand(connection, shape, sorted: false, scalar: true, afterSid: afterSid, pageSize: 1);
                var rows = await ReadRowsAsync(command);
                Assert.InRange(rows.Count, 0, 2);
                if (rows.Count == 0)
                {
                    break;
                }

                actual.Add(rows[0]);
                afterSid = rows[0].Sid;
            }

            Assert.Equal(expected, actual);
            Assert.Equal(actual.Count, actual.Distinct().Count());
        }

        [Theory]
        [InlineData("scope-birthdate-token", false, "year", "day", "december")]
        [InlineData("scope-birthdate-token", true, "year", "day", "december")]
        [InlineData("scope-birthdate-token-filtered", false, "year", "day")]
        [InlineData("scope-birthdate-token-filtered", true, "year", "day")]
        [InlineData("scope-birthdate-token-revinclude", false, "year", "day")]
        [InlineData("scope-birthdate-token-revinclude", true, "year", "day")]
        [InlineData("scope-birthdate-token-organization", false, "year")]
        [InlineData("scope-birthdate-token-organization", true, "year")]
        public async Task GivenSmartScopesWithPatientBirthdateAndGender_WhenSearched_ThenEachScopeConjunctionIsPreserved(
            string shape,
            bool scalar,
            params string[] expected)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            await SeedCompositionWitnessesAsync(connection);
            using var command = CreateCommand(connection, shape, sorted: false, scalar: scalar);

            var rows = await ReadRowsAsync(command);

            Assert.Equal(expected.Order(), rows.Where(r => r.IsMatch).Select(r => r.Id).Order());
            Assert.Equal(rows.Count, rows.Distinct().Count());
            if (shape == "scope-birthdate-token-revinclude")
            {
                Assert.Equal(expected.Select(id => "observation-" + id).Order(), rows.Where(r => !r.IsMatch).Select(r => r.Id).Order());
            }

            using var count = CreateCommand(connection, shape, sorted: false, scalar: scalar, countOnly: true);
            Assert.Equal(expected.Length, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }

        [Theory]
        [InlineData("organization", false)]
        [InlineData("organization", true)]
        [InlineData("organization-repeated", false)]
        [InlineData("organization-repeated", true)]
        [InlineData("organization-revinclude", false)]
        [InlineData("organization-revinclude", true)]
        public async Task GivenPatientBirthdateAndAnOrganizationChain_WhenPaged_ThenBothPredicatesAndIncludesRemainBound(string shape, bool scalar)
        {
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            await SeedAsync(connection);
            await SeedCompositionWitnessesAsync(connection);
            var matches = new List<string>();
            long? afterSid = null;

            for (int page = 0; page < 3; page++)
            {
                using var command = CreateCommand(connection, shape, sorted: false, scalar: scalar, afterSid: afterSid, pageSize: 1);
                var rows = await ReadRowsAsync(command);
                var pageMatches = rows.Where(r => r.IsMatch).ToList();
                Assert.InRange(pageMatches.Count, 0, 2);
                Assert.Equal(rows.Count, rows.Distinct().Count());
                if (pageMatches.Count == 0)
                {
                    break;
                }

                var match = pageMatches[0];
                matches.Add(match.Id);
                afterSid = match.Sid;
                if (shape == "organization-revinclude")
                {
                    Assert.Equal(["observation-" + match.Id], rows.Where(r => !r.IsMatch).Select(r => r.Id));
                }
            }

            Assert.Equal(["year", "month"], matches);
            using var count = CreateCommand(connection, shape, sorted: false, scalar: scalar, countOnly: true);
            Assert.Equal(2, Convert.ToInt32(await count.ExecuteScalarAsync()));
        }

        private static Expression Scope(SearchParameterInfo typeParameter, string resourceType, params Expression[] parameters)
        {
            var predicates = parameters.Select(parameter =>
            {
                var predicate = Expression.And(
                    Expression.SearchParameter(typeParameter, Expression.StringEquals(FieldName.TokenCode, null, resourceType, false)),
                    parameter);
                predicate.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                return predicate;
            });
            var scope = Expression.And(predicates.ToArray());
            scope.IsSmartV2UnionExpressionForScopesSearchParameters = true;
            return scope;
        }

        private static Expression Equality(SearchParameterInfo parameter, string date)
        {
            var range = DateTimeSearchValue.Parse(date);
            return Expression.SearchParameter(parameter, Expression.And(
                Expression.GreaterThanOrEqual(FieldName.DateTimeStart, null, range.Start),
                Expression.LessThanOrEqual(FieldName.DateTimeEnd, null, range.End)));
        }

        private static SqlCommand CreateCommand(
            SqlConnection connection,
            string shape,
            bool sorted,
            bool scalar,
            bool countOnly = false,
            string query = "2016-07-06",
            long? afterSid = null,
            int pageSize = 100,
            string rootResourceType = "Patient",
            string searchCode = "birthdate",
            bool containment = false)
        {
            var dateParameter = DateDefinitions.GetSearchParameter(rootResourceType, searchCode);
            var model = Substitute.For<ISqlServerFhirModel>();
            model.ResourceTypeIdRange.Returns(((short)1, (short)5));
            model.GetResourceTypeId("Patient").Returns((short)1);
            model.GetResourceTypeId("Observation").Returns((short)2);
            model.GetResourceTypeId("Organization").Returns((short)4);
            model.GetResourceTypeId("ClaimResponse").Returns((short)5);
            model.GetResourceTypeName(1).Returns("Patient");
            model.GetResourceTypeName(2).Returns("Observation");
            model.GetResourceTypeName(4).Returns("Organization");
            model.GetResourceTypeName(5).Returns("ClaimResponse");
            var typeParameter = new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType);
            var definitions = Substitute.For<ISearchParameterDefinitionManager>();
            definitions.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType).Returns(typeParameter);
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            model.TryGetResourceTypeId("Patient", out Arg.Any<short>()).Returns(call =>
            {
                call[1] = (short)1;
                return true;
            });
            model.TryGetResourceTypeId("Observation", out Arg.Any<short>()).Returns(call =>
            {
                call[1] = (short)2;
                return true;
            });
            model.TryGetResourceTypeId("Organization", out Arg.Any<short>()).Returns(call =>
            {
                call[1] = (short)4;
                return true;
            });
            model.TryGetResourceTypeId("ClaimResponse", out Arg.Any<short>()).Returns(call =>
            {
                call[1] = (short)5;
                return true;
            });
            model.GetSearchParamId(Birthdate.Url).Returns((short)10);
            model.GetSearchParamId(Subject.Url).Returns((short)11);
            model.GetSearchParamId(Code.Url).Returns((short)12);
            model.GetSearchParamId(Gender.Url).Returns((short)14);
            model.GetSearchParamId(Organization.Url).Returns((short)15);
            model.GetSearchParamId(OrganizationIdentifier.Url).Returns((short)16);
            model.GetSearchParamId(PaymentDate.Url).Returns((short)17);
            Expression dateEquality = Equality(dateParameter, query);
            var male = Expression.SearchParameter(Gender, Expression.StringEquals(FieldName.TokenCode, null, "male", false));
            string resourceType = shape is "forward" or "include" ? "Observation" : rootResourceType;
            Expression expression = shape switch
            {
                "forward" or "include" => Expression.Chained(["Observation"], Subject, ["Patient"], false, dateEquality),
                "reverse" => Expression.And(
                    Expression.Chained(
                        ["Observation"],
                        Subject,
                        ["Patient"],
                        true,
                        Expression.SearchParameter(Code, Expression.StringEquals(FieldName.TokenCode, null, "match", false))),
                    dateEquality),
                "or" => Expression.SearchParameter(dateParameter, Expression.Or(
                    ((SearchParameterExpression)dateEquality).Expression,
                    ((SearchParameterExpression)Equality(dateParameter, "2016-07-31")).Expression)),
                "repeated" => Expression.And(dateEquality, Equality(dateParameter, "2016-07-31")),
                _ => dateEquality,
            };

            if (shape is "organization" or "organization-repeated" or "organization-revinclude" or "scope-birthdate-token-organization")
            {
                expression = Expression.And(
                    expression,
                    Expression.Chained(
                        ["Patient"],
                        Organization,
                        ["Organization"],
                        false,
                        Expression.SearchParameter(OrganizationIdentifier, Expression.StringEquals(FieldName.TokenCode, null, "allowed-org", false))));
                if (shape == "organization-repeated")
                {
                    expression = Expression.And(expression, Equality(Birthdate, "2016-07-31"));
                }
            }

            if (shape is "scope-birthdate-token" or "scope-birthdate-token-filtered" or "scope-birthdate-token-revinclude" or "scope-birthdate-token-organization")
            {
                var scopes = new List<Expression>
                {
                    Scope(typeParameter, "Patient", Equality(Birthdate, "2016-07-06"), male),
                    Scope(typeParameter, "Patient", Equality(Birthdate, "2016-12-31"), Expression.SearchParameter(Gender, Expression.StringEquals(FieldName.TokenCode, null, "female", false))),
                    Scope(typeParameter, "Patient", Equality(Birthdate, "2016-12-31"), male),
                };
                if (shape == "scope-birthdate-token-revinclude")
                {
                    scopes.Add(Expression.And(Expression.SearchParameter(typeParameter, Expression.StringEquals(FieldName.TokenCode, null, "Observation", false))));
                }

                var union = Expression.Union(UnionOperator.All, scopes);
                union.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                expression = shape == "scope-birthdate-token" ? union : Expression.And(union, expression);
            }

            if (shape is "scope" or "scope-revinclude")
            {
                var individual = Expression.And(Expression.SearchParameter(Code, Expression.StringEquals(FieldName.TokenCode, null, "allowed", false)));
                individual.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                var scope = Expression.And(individual);
                scope.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                var union = Expression.Union(UnionOperator.All, [scope]);
                union.IsSmartV2UnionExpressionForScopesSearchParameters = true;
                expression = Expression.And(union, expression);
            }

            if (shape is "include" or "revinclude" or "scope-revinclude" or "organization-revinclude" or "scope-birthdate-token-revinclude")
            {
                bool scoped = shape == "scope-revinclude";
                expression = Expression.And(expression, new IncludeExpression(
                    [resourceType],
                    Subject,
                    "Observation",
                    "Patient",
                    null,
                    false,
                    shape != "include",
                    false,
                    allowedResourceTypesByScope: scoped ? [KnownResourceTypes.All] : shape == "scope-birthdate-token-revinclude" ? ["Observation"] : null));
            }

            expression = Expression.And(
                Expression.SearchParameter(
                    typeParameter,
                    Expression.StringEquals(FieldName.TokenCode, null, resourceType, false)),
                expression);

            if (afterSid.HasValue)
            {
                expression = Expression.And(
                    Expression.SearchParameter(
                        SqlSearchParameters.PrimaryKeyParameter,
                        Expression.GreaterThan(SqlFieldName.PrimaryKey, null, new PrimaryKeyValue(model.GetResourceTypeId(resourceType), afterSid.Value))),
                    expression);
            }

            var factory = new SearchParamTableExpressionQueryGeneratorFactory(new SearchParameterToSearchValueTypeMap());
            var options = new SqlSearchOptions(new SearchOptions
            {
                SearchParameters = [],
                UnsupportedSearchParams = [],
                Sort = sorted
                    ? [(dateParameter, SortOrder.Ascending)]
                    : [(new SearchParameterInfo(SearchParameterNames.LastUpdated, SearchParameterNames.LastUpdated), SortOrder.Ascending)],
                MaxItemCount = pageSize,
                IncludeCount = 100,
                ResourceVersionTypes = ResourceVersionType.Latest,
                CountOnly = countOnly,
            });
            if (countOnly)
            {
                expression = expression.AcceptVisitor(FlatteningRewriter.Instance).AcceptVisitor(RemoveIncludesRewriter.Instance);
            }

            var root = (SqlRootExpression)SqlServerSearchService.ApplyDateEqualitySemantics(expression, containment, scalar)
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

        private static async Task<List<(string Id, bool IsMatch, long Sid)>> ReadRowsAsync(SqlCommand command)
        {
            using var reader = await command.ExecuteReaderAsync();
            var rows = new List<(string, bool, long)>();
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetString(reader.GetOrdinal("ResourceId")),
                    reader.GetBoolean(reader.GetOrdinal("IsMatch")),
                    reader.GetInt64(reader.GetOrdinal("ResourceSurrogateId"))));
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
                ("year", "2016"),
                ("month", "2016-07"),
                ("day", "2016-07-06"),
                ("month-end", "2016-07-31"),
                ("december", "2016-12"),
                ("next-day", "2016-07-07"),
                ("next-year", "2017"),
                ("leap-year", "2000"),
                ("leap-month", "2000-02"),
                ("leap-day", "2000-02-29"),
                ("min-year", "0001"),
                ("min-month", "0001-01"),
                ("min-day", "0001-01-01"),
                ("max-year", "9999"),
                ("max-month", "9999-12"),
                ("max-day", "9999-12-31"),
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
                    INSERT #TokenSearchParam VALUES (2, @sid + 100, 12, 'match', NULL, NULL);
                    IF @id IN ('year', 'day')
                        INSERT #TokenSearchParam VALUES (1, @sid, 12, 'allowed', NULL, NULL), (2, @sid + 100, 12, 'allowed', NULL, NULL);
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
                    SELECT 5, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted FROM #Resource WHERE ResourceTypeId = 1;
                INSERT #DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay)
                    SELECT 5, ResourceSurrogateId, 17, StartDateTime, EndDateTime, IsLongerThanADay
                    FROM #DateTimeSearchParam WHERE ResourceTypeId = 1 AND SearchParamId = 10;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task SeedCompositionWitnessesAsync(SqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                    VALUES (1, 20, 'missing', 0, 0), (2, 120, 'observation-missing', 0, 0),
                           (1, 21, 'missing-female', 0, 0), (2, 123, 'observation-missing-female', 0, 0),
                           (1, 90, 'year', 1, 0),
                           (4, 301, 'org', 0, 0), (4, 302, 'wrong-org', 0, 0);
                INSERT #ReferenceSearchParam VALUES
                    (2, 120, 11, 1, 'missing', NULL),
                    (2, 123, 11, 1, 'missing-female', NULL);
                INSERT #ReferenceSearchParam
                    SELECT 1, ResourceSurrogateId, 15, 4,
                        CASE WHEN ResourceId = 'day' THEN 'wrong-org' ELSE 'org' END, NULL
                    FROM #Resource WHERE ResourceTypeId = 1 AND IsHistory = 0;
                INSERT #TokenSearchParam VALUES (4, 301, 16, 'allowed-org', NULL, NULL), (4, 302, 16, 'other-org', NULL, NULL);
                INSERT #DateTimeSearchParam (ResourceTypeId, ResourceSurrogateId, SearchParamId, StartDateTime, EndDateTime, IsLongerThanADay)
                    SELECT target.ResourceTypeId, target.Sid, 99, dates.StartDateTime, dates.EndDateTime, dates.IsLongerThanADay
                    FROM #DateTimeSearchParam dates CROSS JOIN (VALUES (1, 20)) target(ResourceTypeId, Sid)
                    WHERE dates.ResourceTypeId = 1 AND dates.ResourceSurrogateId = 3;
                INSERT #TokenSearchParam
                    SELECT 1, ResourceSurrogateId, 14,
                        CASE WHEN ResourceId IN ('month', 'december', 'missing-female') THEN 'female' ELSE 'male' END, NULL, NULL
                    FROM #Resource WHERE ResourceTypeId = 1;
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
}
