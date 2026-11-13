// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Search
{
    /// <summary>
    /// Verifies calendar-date search results through the storage fixture's production parser and search service.
    /// SMART scopes are injected request context, not authenticated HTTP grants.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait("DataStore", "SqlServer")]
    public class SqlCalendarDateStorageTests : IClassFixture<SqlCalendarDateStorageTests.CalendarDateStorageFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly FhirSqlServerConfiguration _configuration;
        private readonly bool _originalScalarSetting;
        private readonly bool _originalContainmentSetting;
        private readonly IFhirRequestContext _originalRequestContext;
        private ResourceWrapperFactory _wrapperFactory;

        static SqlCalendarDateStorageTests()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlCalendarDateStorageTests"/> class.
        /// </summary>
        /// <param name="fixture">The class-scoped SQL storage fixture.</param>
        public SqlCalendarDateStorageTests(CalendarDateStorageFixture fixture)
        {
            _fixture = fixture;
            _configuration = fixture.Service.GetRequiredService<FhirSqlServerConfiguration>();
            _originalScalarSetting = _configuration.EnableScalarTemporalEqualityRewriter;
            _originalContainmentSetting = _configuration.EnableFhirDateContainment;
            _originalRequestContext = fixture.FhirRequestContextAccessor.RequestContext;
            fixture.FhirRequestContextAccessor.RequestContext = new FhirRequestContext(
                "PUT",
                "http://localhost/",
                "http://localhost/",
                nameof(SqlCalendarDateStorageTests),
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>());
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            Assert.Equal(TemporalType.Date, _fixture.SearchParameterDefinitionManager.GetSearchParameter("Patient", "birthdate").TemporalType);
            Assert.Equal(TemporalType.Date, _fixture.SearchParameterDefinitionManager.GetSearchParameter("ClaimResponse", "payment-date").TemporalType);
            var parser = new ReferenceSearchValueParser(_fixture.FhirRequestContextAccessor, new FhirServerInstanceConfiguration(), ResourceIdPolicy.Standard);
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);
            var converters = typeof(ITypedElementToSearchValueConverter).Assembly.GetTypes()
                .Where(type => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface
                    && type != typeof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter))
                .Select(type => (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, parser, codeSystemResolver));
            var indexer = new TypedElementSearchIndexer(
                _fixture.SupportedSearchParameterDefinitionManager,
                new FhirTypedElementToSearchValueConverterManager(converters),
                new LightweightReferenceToElementResolver(parser, ModelInfoProvider.Instance),
                ModelInfoProvider.Instance,
                new FirelyFhirPathProvider(),
                NullLogger<TypedElementSearchIndexer>.Instance,
                Substitute.For<IFailureMetricHandler>());
            var compartments = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            await compartments.StartAsync(CancellationToken.None);
            var claims = Substitute.For<IClaimsExtractor>();
            claims.Extract().Returns(Array.Empty<KeyValuePair<string, string>>());
            _wrapperFactory = new ResourceWrapperFactory(
                new RawResourceFactory(new FhirJsonSerializer()),
                _fixture.FhirRequestContextAccessor,
                indexer,
                claims,
                new CompartmentIndexer(compartments),
                _fixture.SearchParameterDefinitionManager,
                _fixture.Deserializer);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            _configuration.EnableScalarTemporalEqualityRewriter = _originalScalarSetting;
            _configuration.EnableFhirDateContainment = _originalContainmentSetting;
            _fixture.FhirRequestContextAccessor.RequestContext = _originalRequestContext;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Payment dates retain day, month, and year matches under the selected equality semantics.
        /// </summary>
        /// <param name="scalarEnabled">Whether exact-day scalar rewriting is enabled.</param>
        /// <param name="containment">Whether equality uses containment instead of overlap.</param>
        /// <returns>The test task.</returns>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task GivenStoredPaymentDates_WhenSearchingCalendarDateEquality_ThenResultsAndCountsPreserveSelectedSemantics(
            bool scalarEnabled,
            bool containment)
        {
            _configuration.EnableScalarTemporalEqualityRewriter = scalarEnabled;
            _configuration.EnableFhirDateContainment = containment;
            string prefix = $"payment-{scalarEnabled}-{containment}-";
            (string Id, string Date)[] dates =
            [
                ("year", "2016"),
                ("month", "2016-07"),
                ("day", "2016-07-06"),
                ("next-day", "2016-07-07"),
                ("wrong-month", "2016-08"),
                ("wrong-year", "2017"),
                ("missing", null),
            ];
            var resources = dates.Select(date => new ClaimResponse
            {
                Id = prefix + date.Id,
                Payment = date.Date == null ? null : new ClaimResponse.PaymentComponent { Date = date.Date },
            }).ToArray();
            await UpsertResourcesAsync(resources);

            (string Query, string[] Matches)[] cases =
            [
                ("2016-07-06", containment ? ["day"] : ["year", "month", "day"]),
                ("2016-07", containment ? ["month", "day", "next-day"] : ["year", "month", "day", "next-day"]),
                ("2016", ["year", "month", "day", "next-day", "wrong-month"]),
            ];
            foreach (var testCase in cases)
            {
                var query = new List<Tuple<string, string>>
                {
                    Tuple.Create("_id", string.Join(",", resources.Select(resource => resource.Id))),
                    Tuple.Create("payment-date", testCase.Query),
                    Tuple.Create("_total", "accurate"),
                };

                var result = await _fixture.SearchService.SearchAsync("ClaimResponse", query, CancellationToken.None);

                AssertResults(result, testCase.Matches.Select(id => "ClaimResponse/" + prefix + id).ToArray());
                Assert.Equal(testCase.Matches.Length, result.TotalCount);
                query.Add(Tuple.Create("_summary", "count"));
                var count = await _fixture.SearchService.SearchAsync("ClaimResponse", query, CancellationToken.None);
                AssertResults(count, Array.Empty<string>());
                Assert.Equal(testCase.Matches.Length, count.TotalCount);
            }
        }

        /// <summary>
        /// Sorted day-equality pages preserve the selected semantics and surrogate-ID tie order across scalar modes.
        /// </summary>
        /// <param name="containment">Whether equality uses containment instead of overlap.</param>
        /// <param name="descending">Whether birthdates are sorted in descending order.</param>
        /// <returns>The test task.</returns>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task GivenStoredCalendarDates_WhenPagingSortedDayEquality_ThenSelectedSemanticsAndOrderArePreserved(
            bool containment,
            bool descending)
        {
            string prefix = $"page-{Guid.NewGuid():N}-";
            var year = new Patient { Id = prefix + "year", BirthDate = "2016" };
            var month = new Patient { Id = prefix + "month", BirthDate = "2016-07" };
            var patients = new[]
            {
                new Patient { Id = prefix + "day-z", BirthDate = "2016-07-06" },
                month,
                new Patient { Id = prefix + "day-a", BirthDate = "2016-07-06" },
                year,
                new Patient { Id = prefix + "day-m", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "adjacent", BirthDate = "2016-07-07" },
            };
            await UpsertResourcesAsync(patients);

            var storedTies = new List<ResourceWrapper>();
            foreach (Patient patient in patients.Where(patient => patient.BirthDate == "2016-07-06"))
            {
                storedTies.Add(await _fixture.DataStore.GetAsync(new ResourceKey("Patient", patient.Id), CancellationToken.None));
            }

            string[] tiedIds = storedTies.OrderBy(resource => resource.ResourceSurrogateId)
                .Select(resource => resource.ResourceTypeName + "/" + resource.ResourceId).ToArray();
            string[] expectedIds = containment
                ? tiedIds
                : descending
                    ? tiedIds.Concat(new[] { "Patient/" + month.Id, "Patient/" + year.Id }).ToArray()
                    : new[] { "Patient/" + year.Id, "Patient/" + month.Id }.Concat(tiedIds).ToArray();
            string[] scalarOffIds = null;

            foreach (bool scalarEnabled in new[] { false, true })
            {
                _configuration.EnableFhirDateContainment = containment;
                _configuration.EnableScalarTemporalEqualityRewriter = scalarEnabled;
                var actualIds = new List<string>();
                var seenTokens = new HashSet<string>(StringComparer.Ordinal);
                string token = null;
                for (int pageIndex = 0; pageIndex < expectedIds.Length + 1; pageIndex++)
                {
                    var query = new List<Tuple<string, string>>
                    {
                        Tuple.Create("_id", string.Join(",", patients.Select(patient => patient.Id))),
                        Tuple.Create("birthdate", "2016-07-06"),
                        Tuple.Create("_sort", descending ? "-birthdate" : "birthdate"),
                        Tuple.Create("_count", "1"),
                    };
                    if (token != null)
                    {
                        query.Add(Tuple.Create("ct", ContinuationTokenEncoder.Encode(token)));
                    }

                    _fixture.FhirRequestContextAccessor.RequestContext = new FhirRequestContext(
                        "GET",
                        "http://localhost/",
                        "http://localhost/",
                        nameof(SqlCalendarDateStorageTests),
                        new Dictionary<string, StringValues>(),
                        new Dictionary<string, StringValues>());

                    SearchResult page = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

                    Assert.Empty(page.UnsupportedSearchParameters);
                    Assert.Empty(page.SearchIssues);
                    Assert.Null(page.IncludesContinuationToken);
                    Assert.InRange(page.Results.Count(), 0, 1);
                    Assert.All(page.Results, entry => Assert.Equal(SearchEntryMode.Match, entry.SearchEntryMode));
                    if (pageIndex == 0)
                    {
                        Assert.False(string.IsNullOrEmpty(page.ContinuationToken));
                    }

                    token = page.ContinuationToken;
                    if (token != null)
                    {
                        Assert.False(string.IsNullOrEmpty(token));
                        Assert.Single(page.Results);
                        Assert.True(seenTokens.Add(token), "Continuation tokens must advance on every page.");
                    }

                    actualIds.AddRange(page.Results.Select(entry => entry.Resource.ResourceTypeName + "/" + entry.Resource.ResourceId));
                    if (token == null)
                    {
                        break;
                    }
                }

                Assert.Null(token);
                Assert.Equal(expectedIds, actualIds);
                Assert.Equal(actualIds.Count, actualIds.Distinct(StringComparer.Ordinal).Count());
                if (scalarEnabled)
                {
                    Assert.Equal(scalarOffIds, actualIds);
                }
                else
                {
                    scalarOffIds = actualIds.ToArray();
                }
            }
        }

        /// <summary>
        /// Patient birthdate and token constraints intersect within a scope and union across scopes.
        /// Reverse includes require an Observation resource-type grant.
        /// </summary>
        /// <param name="scalarEnabled">Whether exact-day scalar rewriting is enabled for this fixture.</param>
        /// <param name="grantObservations">Whether the injected scopes grant Observation access.</param>
        /// <returns>The test task.</returns>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task GivenPatientBirthdateAndTokenScopeUnion_WhenSearchingWithReverseIncludes_ThenOnlyGrantedResourcesMatch(bool scalarEnabled, bool grantObservations)
        {
            _configuration.EnableScalarTemporalEqualityRewriter = scalarEnabled;
            string prefix = $"scope-{scalarEnabled}-{grantObservations}-";
            var patients = new[]
            {
                new Patient { Id = prefix + "year", BirthDate = "2016", Gender = AdministrativeGender.Male },
                new Patient { Id = prefix + "month", BirthDate = "2016-07", Gender = AdministrativeGender.Male },
                new Patient { Id = prefix + "day", BirthDate = "2016-07-06", Gender = AdministrativeGender.Male },
                new Patient { Id = prefix + "other-scope", BirthDate = "2015-01-01", Gender = AdministrativeGender.Female },
                new Patient { Id = prefix + "wrong-gender", BirthDate = "2016-07-06", Gender = AdministrativeGender.Female },
                new Patient { Id = prefix + "wrong-day", BirthDate = "2016-07-07", Gender = AdministrativeGender.Male },
                new Patient { Id = prefix + "missing-date", Gender = AdministrativeGender.Male },
                new Patient { Id = prefix + "missing-gender", BirthDate = "2016-07-06" },
            };
            await UpsertResourcesAsync(patients);
            await UpsertResourcesAsync(patients.Select(patient => new Observation
            {
                Id = patient.Id + "-observation",
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("urn:birthdate-tests", "observation"),
                Subject = new ResourceReference("Patient/" + patient.Id),
            }).ToArray());

            var firstScope = new SearchParams();
            firstScope.Add("birthdate", "2016-07-06");
            firstScope.Add("gender", "male");
            var secondScope = new SearchParams();
            secondScope.Add("birthdate", "2015-01-01");
            var access = _fixture.FhirRequestContextAccessor.RequestContext.AccessControlContext;
            access.ApplyFineGrainedAccessControl = true;
            access.ApplyFineGrainedAccessControlWithSearchParameters = true;
            access.AllowedResourceActions.Add(new ScopeRestriction("Patient", DataActions.Read | DataActions.Search, "system", firstScope));
            access.AllowedResourceActions.Add(new ScopeRestriction("Patient", DataActions.Read | DataActions.Search, "system", secondScope));
            if (grantObservations)
            {
                access.AllowedResourceActions.Add(new ScopeRestriction("Observation", DataActions.Read | DataActions.Search, "system"));
            }

            var result = await _fixture.SearchService.SearchAsync(
                "Patient",
                new[]
                {
                    Tuple.Create("_id", string.Join(",", patients.Select(patient => patient.Id))),
                    Tuple.Create("_revinclude", "Observation:subject"),
                },
                CancellationToken.None);

            string[] includes = grantObservations
                ? new[] { "Observation/" + prefix + "year-observation", "Observation/" + prefix + "month-observation", "Observation/" + prefix + "day-observation", "Observation/" + prefix + "other-scope-observation" }
                : Array.Empty<string>();
            AssertResults(
                result,
                new[] { "Patient/" + prefix + "year", "Patient/" + prefix + "month", "Patient/" + prefix + "day", "Patient/" + prefix + "other-scope" },
                includes);
        }

        /// <summary>
        /// Exact-day Patient birthdate overlap and the Organization chain must both hold for each result and its count.
        /// </summary>
        /// <param name="scalarEnabled">Whether exact-day scalar rewriting is enabled for this fixture.</param>
        /// <returns>The test task.</returns>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenPatientBirthdatesAndOrganizations_WhenSearchingBoth_ThenResultsAndCountPreserveEachBinding(bool scalarEnabled)
        {
            _configuration.EnableScalarTemporalEqualityRewriter = scalarEnabled;
            string prefix = $"organization-{scalarEnabled}-";
            var organization = new Organization { Id = prefix + "match", Identifier = { new Identifier("urn:birthdate-tests", prefix + "match") } };
            var otherOrganization = new Organization { Id = prefix + "other", Identifier = { new Identifier("urn:birthdate-tests", prefix + "other") } };
            var reference = new ResourceReference("Organization/" + organization.Id);
            var patients = new[]
            {
                new Patient { Id = prefix + "year", BirthDate = "2016", ManagingOrganization = reference },
                new Patient { Id = prefix + "month", BirthDate = "2016-07", ManagingOrganization = reference },
                new Patient { Id = prefix + "day", BirthDate = "2016-07-06", ManagingOrganization = reference },
                new Patient { Id = prefix + "wrong-day", BirthDate = "2016-07-07", ManagingOrganization = reference },
                new Patient { Id = prefix + "wrong-month", BirthDate = "2016-08", ManagingOrganization = reference },
                new Patient { Id = prefix + "wrong-year", BirthDate = "2015", ManagingOrganization = reference },
                new Patient { Id = prefix + "missing-date", ManagingOrganization = reference },
                new Patient { Id = prefix + "wrong-organization", BirthDate = "2016-07-06", ManagingOrganization = new ResourceReference("Organization/" + otherOrganization.Id) },
                new Patient { Id = prefix + "missing-organization", BirthDate = "2016-07-06" },
                new Patient { Id = prefix + "dangling-organization", BirthDate = "2016-07-06", ManagingOrganization = new ResourceReference("Organization/" + prefix + "absent") },
            };
            await UpsertResourcesAsync(organization, otherOrganization);
            await UpsertResourcesAsync(patients);
            var query = new List<Tuple<string, string>>
            {
                Tuple.Create("_id", string.Join(",", patients.Select(patient => patient.Id))),
                Tuple.Create("birthdate", "2016-07-06"),
                Tuple.Create("organization.identifier", "urn:birthdate-tests|" + prefix + "match"),
                Tuple.Create("_total", "accurate"),
            };

            var result = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            AssertResults(result, new[] { "Patient/" + prefix + "year", "Patient/" + prefix + "month", "Patient/" + prefix + "day" });
            Assert.Equal(3, result.TotalCount);

            query.Add(Tuple.Create("_summary", "count"));
            var count = await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            AssertResults(count, Array.Empty<string>());
            Assert.Equal(3, count.TotalCount);
        }

        private static void AssertResults(SearchResult result, string[] matches, string[] includes = null)
        {
            Assert.Empty(result.UnsupportedSearchParameters);
            Assert.Empty(result.SearchIssues);
            Assert.Null(result.ContinuationToken);
            Assert.Null(result.IncludesContinuationToken);
            Assert.Equal(matches.Length + (includes?.Length ?? 0), result.Results.Count());
            Assert.Equal(
                matches.OrderBy(id => id, StringComparer.Ordinal),
                result.Results.Where(entry => entry.SearchEntryMode == SearchEntryMode.Match)
                    .Select(entry => entry.Resource.ResourceTypeName + "/" + entry.Resource.ResourceId).OrderBy(id => id, StringComparer.Ordinal));
            Assert.Equal(
                (includes ?? Array.Empty<string>()).OrderBy(id => id, StringComparer.Ordinal),
                result.Results.Where(entry => entry.SearchEntryMode == SearchEntryMode.Include)
                    .Select(entry => entry.Resource.ResourceTypeName + "/" + entry.Resource.ResourceId).OrderBy(id => id, StringComparer.Ordinal));
        }

        private async Task UpsertResourcesAsync(params Resource[] resources)
        {
            foreach (Resource resource in resources)
            {
                await UpsertResourceAsync(resource);
            }
        }

        private async Task UpsertResourceAsync(Resource resource, bool deleted = false)
        {
            resource.Meta = new Meta { LastUpdated = DateTimeOffset.UtcNow };
            var wrapper = _wrapperFactory.Create(resource.ToResourceElement(), deleted, keepMeta: true);
            await _fixture.DataStore.UpsertAsync(
                new ResourceWrapperOperation(wrapper, true, true, null, false, false, null),
                CancellationToken.None);
        }

        /// <summary>
        /// Enables temporal classification before this class's SQL definitions initialize.
        /// </summary>
        public sealed class CalendarDateStorageFixture : FhirStorageTestsFixture
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="CalendarDateStorageFixture"/> class.
            /// </summary>
            public CalendarDateStorageFixture()
                : base(new SqlServerFhirStorageTestsFixture(
                    SchemaVersionConstants.Max,
                    nameof(SqlCalendarDateStorageTests) + Guid.NewGuid().ToString("N"),
                    Options.Create(new CoreFeatureConfiguration
                    {
                        SupportsIncludes = true,
                        EnableSearchParameterTemporalClassification = true,
                    })))
            {
            }
        }
    }
}
