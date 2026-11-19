// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Search
{
    [Collection("Instant date clock")]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlInstantDateTimeSearchTests : IClassFixture<SqlInstantDateTimeSearchTests.InstantSearchFixture>
    {
        private readonly InstantSearchFixture _fixture;

        public SqlInstantDateTimeSearchTests(InstantSearchFixture fixture)
        {
            _fixture = fixture;
        }

        public static IEnumerable<object[]> Flags =>
            from instant in new[] { false, true }
            from containment in new[] { false, true }
            from scalar in new[] { false, true }
            select new object[] { instant, containment, scalar };

        public static IEnumerable<object[]> Prefixes =>
            from prefix in new[] { "eq", "ne", "gt", "ge", "lt", "le", "sa", "eb", "ap" }
            from containment in new[] { false, true }
            from scalar in new[] { false, true }
            select new object[] { prefix, containment, scalar };

        public static IEnumerable<object[]> IneligibleQueries =>
            from url in new[]
            {
                "Patient?birthdate=ge2026-10-01&birthdate=lt2026-10-02",
                "Patient?birthdate=eq2026-10-01",
                "Observation?date=ge2026-10-01&date=lt2026-10-02",
                "Schedule?date=ge2026-10-01",
                "Slot?custom-start=ge2026-10-01",
                "Slot?custom-union=ge2026-10-01",
                "Slot?start:missing=true",
                "?_type=Slot,Observation&_lastUpdated=ge2026-10-01",
            }
            from containment in new[] { false, true }
            from scalar in new[] { false, true }
            select new object[] { url, containment, scalar };

        [Theory]
        [InlineData("Slot", "start", "Slot-start", "Slot.start", true)]
        [InlineData("Slot", "_lastUpdated", "Resource-lastUpdated", "Resource.meta.lastUpdated", true)]
        [InlineData("Patient", "birthdate", "individual-birthdate", "Patient.birthDate", false)]
        [InlineData("Composition", "date", "clinical-date", "Composition.date", false)]
        [InlineData("Schedule", "date", "Schedule-date", "Schedule.planningHorizon", false)]
#if R5
        [InlineData("Encounter", "date", "clinical-date", "Encounter.actualPeriod", false)]
#else
        [InlineData("Encounter", "date", "clinical-date", "Encounter.period", false)]
        [InlineData("Appointment", "date", "Appointment-date", "Appointment.start", true)]
#endif
#if !Stu3
        [InlineData("Bundle", "timestamp", "Bundle-timestamp", "Bundle.timestamp", true)]
#endif
        public void GivenBundledSearchParameter_WhenParsedAndRewritten_ThenOnlyExclusiveInstantDefinitionsChange(
            string resourceType,
            string code,
            string canonicalName,
            string path,
            bool expected)
        {
            var parsed = Assert.IsType<MultiaryExpression>(_fixture.Parse($"{resourceType}?{code}=ge2026-10-01"));
            var original = Assert.IsType<SearchParameterExpression>(Assert.Single(parsed.Expressions));
            SearchParameterInfo definition = _fixture.Definitions.GetSearchParameter(resourceType, code);
            Assert.Same(definition, original.Parameter);
            Assert.True(definition.IsSystemDefined);
            Assert.Equal(new Uri($"http://hl7.org/fhir/SearchParameter/{canonicalName}"), definition.Url);
            Assert.Contains(path, definition.Expression);

            Expression result = original.AcceptVisitor(new InstantDateTimeRewriter());

            Assert.Equal(expected, !ReferenceEquals(original, result));
            using SqlCommand command = _fixture.Generate($"{resourceType}?{code}=ge2026-10-01", instant: true);
            Assert.Equal(!expected, command.CommandText.Contains("EndDateTime", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("Slot.start", true)]
        [InlineData("Slot.end", true)]
        [InlineData("Observation.issued", true)]
        [InlineData("Slot.meta.lastUpdated", true)]
        [InlineData("Bundle.entry.response.lastModified", true)]
        [InlineData("Patient.birthDate", false)]
        [InlineData("Observation.effective", false)]
        [InlineData("Observation.effectiveDateTime", false)]
        [InlineData("Schedule.planningHorizon", false)]
        [InlineData("Appointment.created", false)]
        [InlineData("slot.start", false)]
        [InlineData("Slot.Start", false)]
        [InlineData("Slot.start.first()", false)]
        [InlineData("Slot.start | Slot.end", false)]
#if Stu3
        [InlineData("Bundle.timestamp", false)]
#else
        [InlineData("Bundle.timestamp", true)]
#endif
        public void GivenPathOnlyDefinitionMarkedSystemDefined_WhenCheckingActiveMetadata_ThenOnlyExclusiveInstantPathsChange(string path, bool expected)
        {
            // This bypasses registration to test metadata traversal. A real FHIR path is not necessarily a built-in parameter.
            // Bundle.entry.response.lastModified exercises inline backbone summaries; Slot.meta.lastUpdated resolves Meta.
            var parameter = new SearchParameterInfo("test", "test", ValueSets.SearchParamType.Date, expression: path) { IsSystemDefined = true };
            parameter.TemporalType = SearchParameterTemporalTypeClassifier.Classify(parameter, ModelInfoProvider.Instance);
            var original = Expression.SearchParameter(parameter, Expression.GreaterThan(FieldName.DateTimeEnd, null, DateTimeOffset.MinValue));

            Expression result = original.AcceptVisitor(new InstantDateTimeRewriter());

            Assert.Equal(expected, !ReferenceEquals(original, result));
        }

        [Theory]
        [MemberData(nameof(Flags))]
        public void GivenLiteralSlotRangeAndActor_WhenProductionPipelineRuns_ThenTheFlagControlsOneColumn(
            bool instant,
            bool containment,
            bool scalar)
        {
            const string url = "Slot?start=ge2026-10-01&start=lt2026-10-02&schedule.actor=Location/example";

            using SqlCommand command = _fixture.Generate(url, instant, containment, scalar);
            string sql = command.CommandText;

            Assert.Contains("StartDateTime", sql);
            Assert.Equal(!instant, sql.Contains("EndDateTime", StringComparison.Ordinal));
            Assert.Contains("ReferenceSearchParam", sql);
            Assert.Contains("example", command.Parameters.Cast<SqlParameter>().Select(p => p.Value));
            if (instant)
            {
                Assert.DoesNotContain("UNION ALL", sql);
                Assert.Single(
                    _fixture.Rewrite(url, true, containment, scalar).SearchParamTableExpressions,
                    t => t.QueryGenerator?.Table == SqlServer.Features.Schema.Model.VLatest.DateTimeSearchParam);
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void GivenCoreAndSqlFlags_WhenLiteralSlotRangeIsGenerated_ThenBothMustBeEnabled(
            bool classificationEnabled,
            bool instantEnabled,
            bool expectedRewrite)
        {
            var fixture = new InstantSearchFixture(classificationEnabled);

            using SqlCommand command = fixture.Generate("Slot?start=ge2026-10-01&start=lt2026-10-02", instantEnabled);

            Assert.Contains("StartDateTime", command.CommandText);
            Assert.Equal(!expectedRewrite, command.CommandText.Contains("EndDateTime", StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(Prefixes))]
        public void GivenLiteralPrefix_WhenProductionSqlGenerated_ThenOnlyDateFieldsChange(string prefix, bool containment, bool scalar)
        {
            using var clock = Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(InstantSearchFixture.FrozenNow));
            string url = $"Slot?start={prefix}2026-10-01";

            using SqlCommand original = _fixture.Generate(url, false, containment, scalar);
            using SqlCommand rewritten = _fixture.Generate(url, true, containment, scalar);

            Assert.Contains("StartDateTime", rewritten.CommandText);
            Assert.DoesNotContain("EndDateTime", rewritten.CommandText);
            Assert.Equal(
                original.Parameters.Cast<SqlParameter>().Select(p => p.Value).OrderBy(v => v.ToString()),
                rewritten.Parameters.Cast<SqlParameter>().Select(p => p.Value).OrderBy(v => v.ToString()));

            Expression expected = SqlServerSearchService.ApplyDateEqualitySemantics(_fixture.Parse(url), containment, scalar);
            Assert.Equal(
                DateComparisons(expected).OrderBy(pair => pair.Operator).ThenBy(pair => pair.Value),
                Regex.Matches(rewritten.CommandText, @"\bStartDateTime\s*(>=|<=|<>|=|>|<)\s*(@\w+)")
                    .Select(match => (Operator: match.Groups[1].Value, Value: (DateTime)rewritten.Parameters[match.Groups[2].Value].Value))
                    .OrderBy(pair => pair.Operator).ThenBy(pair => pair.Value));
        }

        [Theory]
        [InlineData("Slot?start=2026-10-01,2026-10-03", 0)]
        [InlineData("Slot?start=ge2026-10-01&start=lt2026-10-02&start=ge2026-09-01", 0)]
        [InlineData("Schedule?_has:Slot:schedule:start=ge2026-10-01", 0)]
        [InlineData("Slot?schedule.actor:Patient.birthdate=2026-10-01&start=ge2026-10-01", 1)]
#if R5
        // R5 resolves Appointment.date to the multi-resource clinical-date union.
        [InlineData("AppointmentResponse?appointment.date=ge2026-10-01&actor=Patient/example", 1)]
#else
        [InlineData("AppointmentResponse?appointment.date=ge2026-10-01&actor=Patient/example", 0)]
#endif
        public void GivenBooleanOrChainScope_WhenProductionSqlGenerated_ThenReferencesAndNonDateBindingsAreUnchanged(string url, int expectedEndPredicates)
        {
            using SqlCommand original = _fixture.Generate(url, false, containment: true);
            using SqlCommand rewritten = _fixture.Generate(url, true, containment: true);

            Assert.Equal(
                original.CommandText.Replace("EndDateTime", "StartDateTime", StringComparison.Ordinal),
                rewritten.CommandText.Replace("EndDateTime", "StartDateTime", StringComparison.Ordinal));
            Assert.Equal(expectedEndPredicates, Regex.Matches(rewritten.CommandText, @"\bEndDateTime\s*(?:>=|<=|<>|=|>|<)\s*@\w+").Count);
        }

        [Theory]
        [MemberData(nameof(IneligibleQueries))]
        public void GivenIneligibleLiteral_WhenProductionSqlGenerated_ThenFlagMakesNoChange(string url, bool containment, bool scalar)
        {
            using SqlCommand original = _fixture.Generate(url, false, containment, scalar);
            using SqlCommand rewritten = _fixture.Generate(url, true, containment, scalar);

            Assert.Equal(original.CommandText, rewritten.CommandText);
        }

        [Fact]
        public void GivenResourceUnionDateParameter_WhenParsed_ThenVersionSpecificBehaviorIsPreserved()
        {
            const string url = "?_type=Appointment,Observation&date=ge2026-10-01";
            if (ModelInfoProvider.Version != FhirSpecification.R5)
            {
                Assert.Throws<Core.Features.Persistence.BadRequestException>(() => _fixture.Parse(url));
                return;
            }

            using SqlCommand original = _fixture.Generate(url, false);
            using SqlCommand rewritten = _fixture.Generate(url, true);
            Assert.Equal(original.CommandText, rewritten.CommandText);
            Assert.Contains("EndDateTime", rewritten.CommandText);
        }

        [Theory]
        [InlineData("eq", "lower,fraction,last,offset")]
        [InlineData("ne", "before,upper,after")]
        [InlineData("gt", "upper,after")]
        [InlineData("ge", "lower,fraction,last,offset,upper,after")]
        [InlineData("lt", "before")]
        [InlineData("le", "before,lower,fraction,last,offset")]
        [InlineData("sa", "upper,after")]
        [InlineData("eb", "before")]
        [InlineData("ap", "before,lower,fraction,last,offset,upper,after")]
        public void GivenPointDataset_WhenPredicatesEvaluated_ThenBothFormsMatchExplicitIds(string prefix, string expectedIds)
        {
            using var clock = Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(InstantSearchFixture.FrozenNow));
            foreach (bool containment in new[] { false, true })
            {
                Expression original = SqlServerSearchService.ApplyDateEqualitySemantics(
                    _fixture.Parse($"Slot?start={prefix}2026-10-01"), containment, false);
                Expression rewritten = original.AcceptVisitor(new InstantDateTimeRewriter());
                string[] expected = expectedIds.Split(',').Order().ToArray();

                Assert.Equal(expected, InstantSearchFixture.Points.Where(p => Evaluate(original, p.Value)).Select(p => p.Key).Order());
                Assert.Equal(expected, InstantSearchFixture.Points.Where(p => Evaluate(rewritten, p.Value)).Select(p => p.Key).Order());
            }
        }

        [Theory]
        [InlineData("Slot?start=ge2026-10-01&start=lt2026-10-02", "lower,fraction,last,offset")]
        [InlineData("Slot?start=eq2026-10-01T00:00:00.0000001Z", "fraction")]
        [InlineData("Slot?start=eq2026-10-01T02:00:00%2B02:00", "lower,fraction,offset")]
        [InlineData("Slot?start=eq0001-01-01T00:00:00.0000000Z", "")]
        [InlineData("Slot?start=eq9999-12-31T23:59:59.9999999Z", "")]
        public void GivenFractionTimezoneOrBoundary_WhenPredicatesEvaluated_ThenExpectedIdsArePreserved(string url, string expectedIds)
        {
            Expression original = _fixture.Parse(url);
            Expression rewritten = original.AcceptVisitor(new InstantDateTimeRewriter());
            string[] expected = expectedIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Order().ToArray();

            Assert.Equal(expected, InstantSearchFixture.Points.Where(p => Evaluate(original, p.Value)).Select(p => p.Key).Order());
            Assert.Equal(expected, InstantSearchFixture.Points.Where(p => Evaluate(rewritten, p.Value)).Select(p => p.Key).Order());
        }

        [Theory]
        [InlineData("0001-01-01T00:00:00.0000000Z")]
        [InlineData("9999-12-31T23:59:59.9999999Z")]
        public void GivenRepresentableEndpoint_WhenExactEqualityIsParsed_ThenItMatchesInBothForms(string literal)
        {
            Expression original = _fixture.Parse($"Slot?start=eq{literal}");
            Expression rewritten = original.AcceptVisitor(new InstantDateTimeRewriter());
            DateTimeOffset point = DateTimeOffset.Parse(literal, CultureInfo.InvariantCulture);

            Assert.True(Evaluate(original, point));
            Assert.True(Evaluate(rewritten, point));
        }

        [Fact]
        [Trait("DataStore", "SqlServer")]
        public async Task GivenPersistedPoints_WhenGeneratedSqlExecutes_ThenBothFlagsReturnExpectedIds()
        {
            SqlAzurePipelinesWorkloadIdentityAuthenticationProvider.ConfigureFromEnvironment();
            using var connection = new SqlConnection(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString));
            await connection.OpenAsync();
            using var seed = connection.CreateCommand();
            seed.CommandText = """
                CREATE TABLE #Resource (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, ResourceId varchar(64),
                    IsHistory bit, IsDeleted bit, Version int DEFAULT 1, RequestMethod varchar(10),
                    IsRawResourceMetaSet bit, SearchParamHash varchar(64), RawResource varbinary(max));
                CREATE TABLE #DateTimeSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    StartDateTime datetime2(7), EndDateTime datetime2(7), IsLongerThanADay bit, IsMin bit, IsMax bit);
                CREATE TABLE #ReferenceSearchParam (
                    ResourceTypeId smallint, ResourceSurrogateId bigint, SearchParamId smallint,
                    ReferenceResourceTypeId smallint NULL, ReferenceResourceId varchar(64), IsHistory bit);
                """;
            await seed.ExecuteNonQueryAsync();
            long sid = 100;
            foreach (var point in InstantSearchFixture.Points)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                        VALUES (@type, @sid, @id, 0, 0);
                    DECLARE @tvp TABLE (StartDateTime datetimeoffset(7), EndDateTime datetimeoffset(7));
                    INSERT INTO @tvp VALUES (@value, @value);
                    INSERT INTO #DateTimeSearchParam
                        SELECT @type, @sid, @param, StartDateTime, EndDateTime, 0, 0, 0 FROM @tvp;
                    """;
                insert.Parameters.AddWithValue("@type", _fixture.Model.GetResourceTypeId("Slot"));
                insert.Parameters.AddWithValue("@sid", sid++);
                insert.Parameters.AddWithValue("@id", point.Key);
                insert.Parameters.AddWithValue("@param", _fixture.Model.GetSearchParamId(_fixture.Definitions.GetSearchParameter("Slot", "start").Url));
                insert.Parameters.AddWithValue("@value", point.Value.ToUniversalTime());
                await insert.ExecuteNonQueryAsync();
            }

            using var references = connection.CreateCommand();
            references.CommandText = """
                INSERT INTO #Resource (ResourceTypeId, ResourceSurrogateId, ResourceId, IsHistory, IsDeleted)
                    VALUES (@scheduleType, 900, 'schedule', 0, 0), (@scheduleType, 901, 'other-schedule', 0, 0);
                INSERT INTO #ReferenceSearchParam
                    SELECT @slotType, ResourceSurrogateId, @scheduleParam, @scheduleType, 'schedule', 0
                    FROM #Resource WHERE ResourceTypeId = @slotType;
                INSERT INTO #ReferenceSearchParam
                    VALUES (@scheduleType, 900, @actorParam, @locationType, 'example', 0),
                           (@scheduleType, 901, @actorParam, @locationType, 'different', 0);
                """;
            references.Parameters.AddWithValue("@slotType", _fixture.Model.GetResourceTypeId("Slot"));
            references.Parameters.AddWithValue("@scheduleType", _fixture.Model.GetResourceTypeId("Schedule"));
            references.Parameters.AddWithValue("@locationType", _fixture.Model.GetResourceTypeId("Location"));
            references.Parameters.AddWithValue("@scheduleParam", _fixture.Model.GetSearchParamId(_fixture.Definitions.GetSearchParameter("Slot", "schedule").Url));
            references.Parameters.AddWithValue("@actorParam", _fixture.Model.GetSearchParamId(_fixture.Definitions.GetSearchParameter("Schedule", "actor").Url));
            await references.ExecuteNonQueryAsync();

            using var invariant = connection.CreateCommand();
            invariant.CommandText = "SELECT COUNT(*) FROM #DateTimeSearchParam WHERE StartDateTime <> EndDateTime";
            Assert.Equal(0, await invariant.ExecuteScalarAsync());
            foreach (bool instant in new[] { false, true })
            {
                foreach (bool containment in new[] { false, true })
                {
                    var queries = new Dictionary<string, string[]>
                    {
                        ["Slot?start=ge2026-10-01&start=lt2026-10-02"] = new[] { "fraction", "last", "lower", "offset" },
                        ["Slot?start=ge2026-10-01&start=lt2026-10-02&schedule.actor=Location/example"] = new[] { "fraction", "last", "lower", "offset" },
                        ["Schedule?_has:Slot:schedule:start=eq2026-10-01&actor=Location/example"] = new[] { "schedule" },
                    };
                    foreach (var query in queries)
                    {
                        using SqlCommand command = _fixture.Generate(query.Key, instant, containment);
                        command.Connection = connection;
                        command.CommandText = command.CommandText
                            .Replace("dbo.DateTimeSearchParam", "#DateTimeSearchParam", StringComparison.Ordinal)
                            .Replace("dbo.ReferenceSearchParam", "#ReferenceSearchParam", StringComparison.Ordinal)
                            .Replace("dbo.Resource", "#Resource", StringComparison.Ordinal);
                        using var reader = await command.ExecuteReaderAsync();
                        var ids = new List<string>();
                        while (await reader.ReadAsync())
                        {
                            ids.Add(reader.GetString(reader.GetOrdinal("ResourceId")));
                        }

                        Assert.Equal(query.Value, ids.Order());
                    }
                }
            }
        }

        private static IEnumerable<(string Operator, DateTime Value)> DateComparisons(Expression expression) => expression switch
        {
            SearchParameterExpression parameter => DateComparisons(parameter.Expression),
            MultiaryExpression multiary => multiary.Expressions.SelectMany(DateComparisons),
            NotExpression not => DateComparisons(not.Expression),
            BinaryExpression { Value: DateTimeOffset value } binary => new[]
            {
                (binary.BinaryOperator switch
                {
                    BinaryOperator.Equal => "=",
                    BinaryOperator.NotEqual => "<>",
                    BinaryOperator.GreaterThan => ">",
                    BinaryOperator.GreaterThanOrEqual => ">=",
                    BinaryOperator.LessThan => "<",
                    BinaryOperator.LessThanOrEqual => "<=",
                    _ => throw new InvalidOperationException(binary.ToString()),
                }, value.UtcDateTime),
            },
            _ => throw new InvalidOperationException(expression.ToString()),
        };

        private static bool Evaluate(Expression expression, DateTimeOffset point) => expression switch
        {
            SearchParameterExpression parameter => Evaluate(parameter.Expression, point),
            MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => and.Expressions.All(e => Evaluate(e, point)),
            MultiaryExpression or => or.Expressions.Any(e => Evaluate(e, point)),
            NotExpression not => !Evaluate(not.Expression, point),
            BinaryExpression { Value: DateTimeOffset value } binary => binary.BinaryOperator switch
            {
                BinaryOperator.Equal => point == value,
                BinaryOperator.NotEqual => point != value,
                BinaryOperator.GreaterThan => point > value,
                BinaryOperator.GreaterThanOrEqual => point >= value,
                BinaryOperator.LessThan => point < value,
                BinaryOperator.LessThanOrEqual => point <= value,
                _ => throw new InvalidOperationException(binary.ToString()),
            },
            _ => throw new InvalidOperationException(expression.ToString()),
        };

        public class InstantSearchFixture
        {
            internal static readonly DateTimeOffset FrozenNow = DateTimeOffset.Parse("2026-11-02T00:00:00Z", CultureInfo.InvariantCulture);

            internal static readonly IReadOnlyDictionary<string, DateTimeOffset> Points = new Dictionary<string, DateTimeOffset>
            {
                ["before"] = DateTimeOffset.Parse("2026-09-30T23:59:59.9999999Z", CultureInfo.InvariantCulture),
                ["lower"] = DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture),
                ["fraction"] = DateTimeOffset.Parse("2026-10-01T00:00:00.0000001Z", CultureInfo.InvariantCulture),
                ["offset"] = DateTimeOffset.Parse("2026-10-01T02:00:00+02:00", CultureInfo.InvariantCulture),
                ["last"] = DateTimeOffset.Parse("2026-10-01T23:59:59.9999999Z", CultureInfo.InvariantCulture),
                ["upper"] = DateTimeOffset.Parse("2026-10-02T00:00:00Z", CultureInfo.InvariantCulture),
                ["after"] = DateTimeOffset.Parse("2026-10-02T00:00:00.0000001Z", CultureInfo.InvariantCulture),
            };

            private readonly ExpressionParser _parser;
            private readonly SearchParamTableExpressionQueryGeneratorFactory _factory = new(new SearchParameterToSearchValueTypeMap());
            private readonly SchemaInformation _schema = new(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            private readonly bool _classificationEnabled;

            public InstantSearchFixture()
                : this(true)
            {
            }

            internal InstantSearchFixture(bool classificationEnabled)
            {
                _classificationEnabled = classificationEnabled;
                Definitions = new SearchParameterDefinitionManager(
                    ModelInfoProvider.Instance,
                    Substitute.For<IMediator>(),
                    Substitute.For<ISearchService>().CreateMockScopeProvider(),
                    Substitute.For<ISearchParameterComparer<SearchParameterInfo>>(),
                    Substitute.For<ISearchParameterStatusDataStore>().CreateMockScopeProvider(),
                    Substitute.For<IFhirDataStore>().CreateMockScopeProvider(),
                    NullLogger<SearchParameterDefinitionManager>.Instance,
                    Options.Create(new CoreFeatureConfiguration { EnableSearchParameterTemporalClassification = classificationEnabled }));
                foreach (var custom in new[]
                {
                    (Code: "custom-start", Path: "Slot.start"),
                    (Code: "custom-union", Path: "Slot.start | Slot.end"),
                    (Code: "custom-date", Path: "Patient.birthDate"),
                })
                {
                    string resourceType = custom.Path.Split('.')[0];
                    var parameter = new SearchParameter
                    {
                        Url = $"http://example.org/SearchParameter/{custom.Code}",
                        Name = custom.Code,
                        Code = custom.Code,
                        Type = Hl7.Fhir.Model.SearchParamType.Date,
                        Expression = custom.Path,
#if Stu3 || R4 || R4B
                        Base = new List<ResourceType?> { Enum.Parse<ResourceType>(resourceType) },
#else
                        Base = new List<VersionIndependentResourceTypesAll?> { Enum.Parse<VersionIndependentResourceTypesAll>(resourceType) },
#endif
                    };
                    Definitions.AddNewSearchParameters(new[] { parameter.ToTypedElement() }, calculateHash: false);
                    Definitions.UpdateSearchParameterStatus(parameter.Url, SearchParameterStatus.Enabled);
                    Assert.False(Definitions.GetSearchParameter(resourceType, custom.Code).IsSystemDefined);
                }

                var instance = Substitute.For<IFhirServerInstanceConfiguration>();
                instance.BaseUri.Returns(new Uri("http://localhost/"));
                var referenceParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor(), instance, ResourceIdPolicy.Standard);
                _parser = new ExpressionParser(() => Definitions, new SearchParameterExpressionParser(referenceParser));
                Model = Substitute.For<ISqlServerFhirModel>();
                short typeId = 1;
                foreach (string resourceType in ModelInfoProvider.GetResourceTypeNames().Order())
                {
                    short id = typeId++;
                    Model.GetResourceTypeId(resourceType).Returns(id);
                    Model.TryGetResourceTypeId(resourceType, out Arg.Any<short>()).Returns(call =>
                    {
                        call[1] = id;
                        return true;
                    });
                }

                short parameterId = 1;
                foreach (SearchParameterInfo parameter in Definitions.AllSearchParameters)
                {
                    Model.GetSearchParamId(parameter.Url).Returns(parameterId++);
                }
            }

            internal ISqlServerFhirModel Model { get; }

            internal SearchParameterDefinitionManager Definitions { get; }

            internal Expression Parse(string url)
            {
                string[] parts = url.Split('?');
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + parts[1]);
                string[] resourceTypes = GetResourceTypes(url);
                return Expression.And(query.SelectMany(pair => pair.Value.Select(value => _parser.Parse(resourceTypes, pair.Key, value))).ToArray());
            }

            internal SqlRootExpression Rewrite(string url, bool instant, bool containment = false, bool scalar = false)
            {
                string[] resources = GetResourceTypes(url);
                Expression expression = Expression.And(
                    Parse(url),
                    _parser.Parse(resources, "_type", string.Join(',', resources)));
                var configuration = new FhirSqlServerConfiguration
                {
                    EnableInstantDateTimeRewriter = instant,
                    EnableFhirDateContainment = containment,
                    EnableScalarTemporalEqualityRewriter = scalar,
                };
                var compartment = new SqlCompartmentSearchRewriter(
                    new Lazy<ICompartmentDefinitionManager>(() => Substitute.For<ICompartmentDefinitionManager>()),
                    new Lazy<ISearchParameterDefinitionManager>(() => Definitions));
                var service = new SqlServerSearchService(
                    Substitute.For<ISearchOptionsFactory>(),
                    Substitute.For<IFhirDataStore>(),
                    Model,
                    new SqlRootExpressionRewriter(_factory),
                    new InstantDateTimeRewriter(),
                    new ChainFlatteningRewriter(_factory),
                    new SortRewriter(_factory),
                    new PartitionEliminationRewriter(Model, _schema, () => Definitions),
                    compartment,
                    new SmartCompartmentSearchRewriter(compartment, new Lazy<ISearchParameterDefinitionManager>(() => Definitions), Options.Create(new CoreFeatureConfiguration())),
                    _factory,
                    Substitute.For<ISqlRetryService>(),
                    Options.Create(new SqlServerDataStoreConfiguration()),
                    configuration,
                    _schema,
                    new FhirRequestContextAccessor(),
                    Substitute.For<ICompressedRawResourceConverter>(),
                    Substitute.For<ISqlQueryHashCalculator>(),
                    Substitute.For<IQueryPlanReuseChecker>(),
                    Options.Create(new CoreFeatureConfiguration { EnableSearchParameterTemporalClassification = _classificationEnabled }),
                    NullLogger<SqlServerSearchService>.Instance);
                var method = typeof(SqlServerSearchService).GetMethod("CreateDefaultSearchExpression", BindingFlags.Instance | BindingFlags.NonPublic);
                return (SqlRootExpression)method.Invoke(service, new object[] { expression, new SqlSearchOptions(CreateOptions()) });
            }

            internal SqlCommand Generate(string url, bool instant, bool containment = false, bool scalar = false)
            {
                SqlRootExpression expression = Rewrite(url, instant, containment, scalar);
                var command = new SqlCommand();
                var builder = new IndentedStringBuilder(new StringBuilder());
                var generator = new SqlQueryGenerator(
                    builder,
                    new HashingSqlQueryParameterManager(new SqlQueryParameterManager(command.Parameters)),
                    Model,
                    _schema,
                    _factory,
                    false,
                    false);
                generator.VisitSqlRoot(expression, CreateOptions());
                SqlCommandSimplifier.RemoveRedundantParameters(builder, command.Parameters, NullLogger.Instance);
                command.CommandText = builder.ToString();
                return command;
            }

            private static SearchOptions CreateOptions() => new()
            {
                Sort = new[] { (new SearchParameterInfo("_lastUpdated", "_lastUpdated"), Core.Features.Search.SortOrder.Ascending) },
                SearchParameters = Array.Empty<SearchParameterInfo>(),
                UnsupportedSearchParams = Array.Empty<Tuple<string, string>>(),
                MaxItemCount = 100,
                ResourceVersionTypes = ResourceVersionType.Latest,
            };

            private static string[] GetResourceTypes(string url)
            {
                string[] parts = url.Split('?');
                return string.IsNullOrEmpty(parts[0])
                    ? Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery("?" + parts[1])["_type"].ToString().Split(',')
                    : parts[0].Split(',');
            }
        }
    }
}
