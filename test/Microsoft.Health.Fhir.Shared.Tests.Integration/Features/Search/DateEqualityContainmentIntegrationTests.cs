// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Search
{
    /// <summary>
    /// SQL-backed integration tests that exercise the date <c>eq</c> equality semantics across the three
    /// mutually-exclusive flag combinations resolved by
    /// <c>SqlServerSearchService.ApplyDateEqualitySemantics</c>. A single seeded data set is queried under
    /// each combination by flipping the live <see cref="FhirSqlServerConfiguration"/> instance shared by the
    /// fixture's search service (flags are read per-query, so no server restart is required):
    /// <list type="bullet">
    /// <item><description><b>C1 — Legacy overlap</b> (<c>EnableFhirDateContainment = false</c>): results
    /// identical to <c>main</c>. A day query matches coarser stored values (month/year/period) by overlap.</description></item>
    /// <item><description><b>C2 — Containment</b> (<c>EnableFhirDateContainment = true</c>,
    /// <c>EnableScalarTemporalEqualityRewriter = false</c>): Core's spec-compliant containment range. A day
    /// query matches only stored values whose implicit range fits inside the one-day window.</description></item>
    /// <item><description><b>C3 — Containment + scalar opt</b> (both flags true): identical results to C2,
    /// but allow-listed <c>birthdate</c> collapses to a single End-only predicate (index optimization).
    /// Result-equivalent to C2; proves the optimization is result-preserving.</description></item>
    /// </list>
    /// None of the combinations emit a temporal <c>UNION ALL</c>; the chained / <c>_has</c> / <c>_sort</c>
    /// shapes below are the ones that historically broke when a day-split union was injected.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public sealed class DateEqualityContainmentIntegrationTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private const string TagSystem = "http://contain-test";

        private readonly FhirStorageTestsFixture _fixture;
        private readonly ISearchService _searchService;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly FhirSqlServerConfiguration _config;
        private readonly bool _originalContainment;
        private readonly bool _originalScalarTemporal;

        // A real search indexer + scoped data store, built in InitializeAsync, so that seeded resources
        // persist genuine DateTimeSearchParam / ReferenceSearchParam / _tag index rows. The fixture's
        // default Mediator upsert path uses a substitute indexer that only extracts SearchParameter.url,
        // which would leave every date/birthdate/_tag query returning empty.
        private ISearchIndexer _searchIndexer;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;

        // Unique per test-method instance (xUnit creates a fresh test class instance per method),
        // so every query is isolated from other tests in the shared integration database.
        private readonly string _tag = Guid.NewGuid().ToString("N");

        public DateEqualityContainmentIntegrationTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _searchService = fixture.SearchService;
            _contextAccessor = fixture.FhirRequestContextAccessor;
            _config = fixture.Service.GetRequiredService<FhirSqlServerConfiguration>();
            _originalContainment = _config.EnableFhirDateContainment;
            _originalScalarTemporal = _config.EnableScalarTemporalEqualityRewriter;
        }

        private enum Combo
        {
            /// <summary>Containment OFF — legacy overlap, identical to main.</summary>
            LegacyOverlap,

            /// <summary>Containment ON, scalar-temporal OFF — Core containment range for all date params.</summary>
            Containment,

            /// <summary>Containment ON, scalar-temporal ON — birthdate End-only optimization, others containment.</summary>
            ContainmentScalar,
        }

        public async Task InitializeAsync()
        {
            var converterManager = await CreateFhirTypedElementToSearchValueConverterManagerAsync();
            _searchIndexer = new TypedElementSearchIndexer(
                _fixture.SupportedSearchParameterDefinitionManager,
                converterManager,
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                NullLogger<TypedElementSearchIndexer>.Instance);
            _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
            _scopedDataStore = _fixture.DataStore.CreateMockScope();
        }

        public Task DisposeAsync()
        {
            // Restore the shared fixture configuration so flag state never leaks between tests.
            _config.EnableFhirDateContainment = _originalContainment;
            _config.EnableScalarTemporalEqualityRewriter = _originalScalarTemporal;
            ClearSmartScope();
            _scopedDataStore?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenObservationsWithMixedDatePrecision_WhenSearchingDateEqExactDay_ThenContainmentDropsCoarseValues()
        {
            string oYear = await SeedObservationDate("1980");
            string oMonth = await SeedObservationDate("1980-05");
            string oDay = await SeedObservationDate("1980-05-11");
            string oSecond = await SeedObservationDate("1980-05-11T16:32:15");
            await SeedObservationPeriod("1980-05-16", "1980-05-17"); // 2-day period; never matches a 1-day query

            // C1 overlap: a one-day query overlaps the year and month ranges too.
            await AssertObservationSearch(
                Combo.LegacyOverlap,
                expected: new[] { oYear, oMonth, oDay, oSecond },
                ("date", "eq1980-05-11"));

            // C2 / C3 containment: only stored ranges that fit inside the one-day window survive.
            await AssertObservationSearch(
                Combo.Containment,
                expected: new[] { oDay, oSecond },
                ("date", "eq1980-05-11"));

            await AssertObservationSearch(
                Combo.ContainmentScalar,
                expected: new[] { oDay, oSecond },
                ("date", "eq1980-05-11"));
        }

        [Fact]
        public async Task GivenObservationsWithMixedDatePrecision_WhenSearchingDateEqMonth_ThenContainmentKeepsContainedValues()
        {
            string oYear = await SeedObservationDate("1980");
            string oMonth = await SeedObservationDate("1980-05");
            string oDay = await SeedObservationDate("1980-05-11");
            string oSecond = await SeedObservationDate("1980-05-11T16:32:15");
            string oPeriod = await SeedObservationPeriod("1980-05-16", "1980-05-17");

            // C1 overlap: the year range also overlaps a one-month window.
            await AssertObservationSearch(
                Combo.LegacyOverlap,
                expected: new[] { oYear, oMonth, oDay, oSecond, oPeriod },
                ("date", "eq1980-05"));

            // C2 / C3 containment: the year is wider than May 1980, so it is excluded; everything inside May survives.
            await AssertObservationSearch(
                Combo.Containment,
                expected: new[] { oMonth, oDay, oSecond, oPeriod },
                ("date", "eq1980-05"));

            await AssertObservationSearch(
                Combo.ContainmentScalar,
                expected: new[] { oMonth, oDay, oSecond, oPeriod },
                ("date", "eq1980-05"));
        }

        [Fact]
        public async Task GivenObservationWithMultiDayPeriod_WhenSearchingDateEqSingleDay_ThenContainmentExcludesPeriod()
        {
            string oYear = await SeedObservationDate("1980");
            string oMonth = await SeedObservationDate("1980-05");
            await SeedObservationDate("1980-05-11");
            string oPeriod = await SeedObservationPeriod("1980-05-16", "1980-05-17");

            // C1 overlap: the period's first day overlaps the query day; year and month overlap too.
            await AssertObservationSearch(
                Combo.LegacyOverlap,
                expected: new[] { oYear, oMonth, oPeriod },
                ("date", "eq1980-05-16"));

            // C2 / C3 containment: a 2-day period can never fit inside a 1-day window, so nothing matches.
            await AssertObservationSearch(
                Combo.Containment,
                expected: Array.Empty<string>(),
                ("date", "eq1980-05-16"));

            await AssertObservationSearch(
                Combo.ContainmentScalar,
                expected: Array.Empty<string>(),
                ("date", "eq1980-05-16"));
        }

        [Fact]
        public async Task GivenPatientsWithMixedBirthdatePrecision_WhenSearchingBirthdateEqExactDay_ThenContainmentDropsCoarseValues()
        {
            string pYear = await SeedPatientBirthdate("1980");
            string pMonth = await SeedPatientBirthdate("1980-05");
            string pDay = await SeedPatientBirthdate("1980-05-11");

            // C1 overlap: a day query matches the year and month birthdates by overlap (== main).
            await AssertPatientSearch(
                Combo.LegacyOverlap,
                expected: new[] { pYear, pMonth, pDay },
                ("birthdate", "eq1980-05-11"));

            // C2 containment two-predicate form: only the exact-day birthdate is contained.
            await AssertPatientSearch(
                Combo.Containment,
                expected: new[] { pDay },
                ("birthdate", "eq1980-05-11"));

            // C3 birthdate End-only optimization: result-equivalent to C2 (single contained day).
            await AssertPatientSearch(
                Combo.ContainmentScalar,
                expected: new[] { pDay },
                ("birthdate", "eq1980-05-11"));
        }

        /// <summary>
        /// The partial-precision keystone (VP2): a year-only stored birthdate is matched by an exact-day
        /// query under legacy overlap, but is excluded once containment is enabled — and the scalar End-only
        /// optimization (C3) preserves that containment behavior. This is the case that proves gating the
        /// behavior change on the containment flag is correct.
        /// </summary>
        [Fact]
        public async Task GivenYearOnlyStoredBirthdate_WhenSearchingBirthdateEqExactDay_ThenMatchesUnderOverlapButNotContainment()
        {
            string pYear = await SeedPatientBirthdate("1980");
            string pMonth = await SeedPatientBirthdate("1980-05");
            await SeedPatientBirthdate("1980-05-11");

            // C1 overlap: the year and month ranges both overlap 1980-05-15.
            HashSet<string> overlap = await SearchIds(Combo.LegacyOverlap, "Patient", ("birthdate", "eq1980-05-15"));
            Assert.Contains(pYear, overlap);
            AssertSameSet(new[] { pYear, pMonth }, overlap, "C1 overlap should match year and month birthdates");

            // C2 containment: nothing fits inside the single day 1980-05-15.
            HashSet<string> containment = await SearchIds(Combo.Containment, "Patient", ("birthdate", "eq1980-05-15"));
            Assert.DoesNotContain(pYear, containment);
            AssertSameSet(Array.Empty<string>(), containment, "C2 containment should exclude all partial-precision birthdates");

            // C3 containment + scalar: the End-only optimization also excludes the year birthdate.
            HashSet<string> containmentScalar = await SearchIds(Combo.ContainmentScalar, "Patient", ("birthdate", "eq1980-05-15"));
            Assert.DoesNotContain(pYear, containmentScalar);
            AssertSameSet(Array.Empty<string>(), containmentScalar, "C3 End-only optimization should match containment, not overlap");
        }

        [Fact]
        public async Task GivenForwardChainOnBirthdate_WhenSearchingDateEqExactDay_ThenContainmentNarrowsChainedPatients()
        {
            // Patients with day- and year-precision birthdates, each with an observation referencing them.
            string pDay = await SeedPatientBirthdate("1980-05-11");
            string pYear = await SeedPatientBirthdate("1980");
            string oDay = await SeedObservationForSubject(pDay, "1980-05-11");
            string oYear = await SeedObservationForSubject(pYear, "1980");

            // Observation?subject:Patient.birthdate=eq1980-05-11
            // C1 overlap: both patients' birthdates match the day query, so both observations are returned.
            await AssertObservationSearch(
                Combo.LegacyOverlap,
                expected: new[] { oDay, oYear },
                ("subject:Patient.birthdate", "eq1980-05-11"));

            // C2 / C3 containment: only the exact-day patient matches, so only its observation is returned.
            await AssertObservationSearch(
                Combo.Containment,
                expected: new[] { oDay },
                ("subject:Patient.birthdate", "eq1980-05-11"));

            await AssertObservationSearch(
                Combo.ContainmentScalar,
                expected: new[] { oDay },
                ("subject:Patient.birthdate", "eq1980-05-11"));
        }

        [Fact]
        public async Task GivenReverseHasChainOnDate_WhenSearchingDateEqExactDay_ThenContainmentNarrowsMatchingPatients()
        {
            // Two patients, each referenced by exactly one observation. The patients' own birthdates are
            // irrelevant here; the reverse _has chain keys off the OBSERVATION's effective date precision.
            string patientWithDayObs = await SeedPatientBirthdate("1980-05-11");
            string patientWithYearObs = await SeedPatientBirthdate("1980-05-11");
            await SeedObservationForSubject(patientWithDayObs, "1980-05-11");
            await SeedObservationForSubject(patientWithYearObs, "1980");

            // Patient?_has:Observation:subject:date=eq1980-05-11
            // C1 overlap: the year-precision observation overlaps the day query, so both patients match.
            await AssertPatientSearch(
                Combo.LegacyOverlap,
                expected: new[] { patientWithDayObs, patientWithYearObs },
                ("_has:Observation:subject:date", "eq1980-05-11"));

            // C2 / C3 containment: only the day-precision observation is contained, so only its patient matches.
            await AssertPatientSearch(
                Combo.Containment,
                expected: new[] { patientWithDayObs },
                ("_has:Observation:subject:date", "eq1980-05-11"));

            await AssertPatientSearch(
                Combo.ContainmentScalar,
                expected: new[] { patientWithDayObs },
                ("_has:Observation:subject:date", "eq1980-05-11"));
        }

        [Fact]
        public async Task GivenDateEqWithSort_WhenSearchingAcrossCombos_ThenResultsAreValidAndOrdered()
        {
            string oYear = await SeedObservationDate("1980");
            string oMonth = await SeedObservationDate("1980-05");
            string oDay = await SeedObservationDate("1980-05-11");
            string oSecond = await SeedObservationDate("1980-05-11T16:32:15");

            // date=eq1980-05-11 layered with a sort clause — the shape that threw during SQL generation when a
            // temporal UNION sat under the sort. We sort by _lastUpdated rather than date because the in-proc
            // integration fixture cannot service a date-search-param sort underneath a token (_tag) filter
            // (a pre-existing harness limitation, unrelated to containment — it reproduces with no eq filter).
            // Union-absence under a date-param sort is proven deterministically at the unit level
            // (ScalarTemporalEqualityRewriterTests + SqlServerSearchServiceTests assert no UnionExpression is emitted).
            // Here we verify the eq-containment predicate composes with a sort clause without error and yields the
            // correct set in every flag state.
            await AssertObservationSearch(
                Combo.LegacyOverlap,
                expected: new[] { oYear, oMonth, oDay, oSecond },
                ("date", "eq1980-05-11"),
                ("_sort", "_lastUpdated"));

            await AssertObservationSearch(
                Combo.Containment,
                expected: new[] { oDay, oSecond },
                ("date", "eq1980-05-11"),
                ("_sort", "_lastUpdated"));

            await AssertObservationSearch(
                Combo.ContainmentScalar,
                expected: new[] { oDay, oSecond },
                ("date", "eq1980-05-11"),
                ("_sort", "_lastUpdated"));
        }

        /// <summary>
        /// The SMART V2 granular-scope union is the load-bearing union we deliberately KEEP (shared
        /// authorization infrastructure, distinct from the temporal day-split union that was removed). Driving a
        /// search-parameter-scoped Patient scope causes <c>SearchOptionsFactory</c> to AND a
        /// <c>UnionExpression</c> (<c>IsSmartV2UnionExpressionForScopesSearchParameters = true</c>) beside the
        /// user's <c>birthdate=eq</c> predicate — the exact composite shape (SMART union + date-eq) that
        /// historically broke SQL generation when a temporal union was also present. We assert it now produces
        /// valid SQL and the spec-correct result set in all three flag states, proving the SMART union composes
        /// cleanly with containment and that the VP2 containment behavior holds inside the union.
        /// </summary>
        [Fact]
        public async Task GivenSmartV2ScopeUnion_WhenSearchingBirthdateEqAcrossCombos_ThenUnionComposesWithContainment()
        {
            string pYear = await SeedPatientBirthdate("1980");
            string pMonth = await SeedPatientBirthdate("1980-05");
            string pDay = await SeedPatientBirthdate("1980-05-11");

            // C1 overlap (== main): the SMART union restricts to the tagged patients; the day query then matches
            // the year and month birthdates by overlap.
            HashSet<string> overlap = await SearchIdsWithSmartScope(Combo.LegacyOverlap, ("birthdate", "eq1980-05-11"));
            AssertSameSet(new[] { pYear, pMonth, pDay }, overlap, "C1 SMART-union overlap should match all tagged birthdates");

            // C2 containment: only the exact-day birthdate fits inside the one-day window.
            HashSet<string> containment = await SearchIdsWithSmartScope(Combo.Containment, ("birthdate", "eq1980-05-11"));
            AssertSameSet(new[] { pDay }, containment, "C2 SMART-union containment should drop coarse birthdates");

            // C3 containment + scalar End-only optimization: result-equivalent to C2, still inside the SMART union.
            HashSet<string> containmentScalar = await SearchIdsWithSmartScope(Combo.ContainmentScalar, ("birthdate", "eq1980-05-11"));
            AssertSameSet(new[] { pDay }, containmentScalar, "C3 SMART-union End-only optimization should match containment");
        }

        private void Apply(Combo combo)
        {
            switch (combo)
            {
                case Combo.LegacyOverlap:
                    // Scalar-temporal ON mirrors main's default; it is ignored because containment is OFF.
                    _config.EnableFhirDateContainment = false;
                    _config.EnableScalarTemporalEqualityRewriter = true;
                    break;
                case Combo.Containment:
                    _config.EnableFhirDateContainment = true;
                    _config.EnableScalarTemporalEqualityRewriter = false;
                    break;
                case Combo.ContainmentScalar:
                    _config.EnableFhirDateContainment = true;
                    _config.EnableScalarTemporalEqualityRewriter = true;
                    break;
            }
        }

        private async Task AssertObservationSearch(Combo combo, IReadOnlyCollection<string> expected, params (string Name, string Value)[] query)
        {
            HashSet<string> actual = await SearchIds(combo, "Observation", query);
            AssertSameSet(expected, actual, $"Observation search under {combo} for [{Describe(query)}]");
        }

        private async Task AssertPatientSearch(Combo combo, IReadOnlyCollection<string> expected, params (string Name, string Value)[] query)
        {
            HashSet<string> actual = await SearchIds(combo, "Patient", query);
            AssertSameSet(expected, actual, $"Patient search under {combo} for [{Describe(query)}]");
        }

        private async Task<HashSet<string>> SearchIds(Combo combo, string resourceType, params (string Name, string Value)[] query)
        {
            Apply(combo);

            var parameters = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("_tag", $"{TagSystem}|{_tag}"),
                new Tuple<string, string>("_count", "100"),
            };

            foreach ((string name, string value) in query)
            {
                parameters.Add(new Tuple<string, string>(name, value));
            }

            SearchResult result = await _searchService.SearchAsync(resourceType, parameters, CancellationToken.None);
            return result.Results.Select(r => r.Resource.ResourceId).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// Runs a Patient search under a SMART V2 granular scope (<c>Patient</c> restricted by the test's
        /// isolation <c>_tag</c>), which forces <c>SearchOptionsFactory</c> to emit the kept SMART union beside
        /// the user query. The scope's <c>_tag</c> restriction is what isolates this test's data, so the user
        /// query carries only <c>_count</c> plus the supplied predicates.
        /// </summary>
        private async Task<HashSet<string>> SearchIdsWithSmartScope(Combo combo, params (string Name, string Value)[] query)
        {
            Apply(combo);
            ApplySmartV2PatientScope();

            try
            {
                var parameters = new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("_count", "100"),
                };

                foreach ((string name, string value) in query)
                {
                    parameters.Add(new Tuple<string, string>(name, value));
                }

                SearchResult result = await _searchService.SearchAsync("Patient", parameters, CancellationToken.None);
                return result.Results.Select(r => r.Resource.ResourceId).ToHashSet(StringComparer.Ordinal);
            }
            finally
            {
                ClearSmartScope();
            }
        }

        private void ApplySmartV2PatientScope()
        {
            var accessControl = new AccessControlContext
            {
                ApplyFineGrainedAccessControl = true,
                ApplyFineGrainedAccessControlWithSearchParameters = true,
            };

            var scopeSearchParameters = new SearchParams();
            scopeSearchParameters.Add("_tag", $"{TagSystem}|{_tag}");
            accessControl.AllowedResourceActions.Add(
                new ScopeRestriction("Patient", DataActions.Read, "user", scopeSearchParameters));

            _contextAccessor.RequestContext = new DefaultFhirRequestContext
            {
                BaseUri = new Uri("http://localhost/"),
                CorrelationId = Guid.NewGuid().ToString(),
                RequestHeaders = new Dictionary<string, StringValues>(),
                ResponseHeaders = new Dictionary<string, StringValues>(),
                Method = "GET",
                Uri = new Uri("http://localhost/Patient"),
                AccessControlContext = accessControl,
            };
        }

        private void ClearSmartScope()
        {
            // Reset the AsyncLocal request context to a benign default so the SMART scope never leaks into the
            // non-SMART tests (which run as a system context with no fine-grained access control).
            _contextAccessor.RequestContext = new DefaultFhirRequestContext
            {
                BaseUri = new Uri("http://localhost/"),
                CorrelationId = Guid.NewGuid().ToString(),
                RequestHeaders = new Dictionary<string, StringValues>(),
                ResponseHeaders = new Dictionary<string, StringValues>(),
                Method = "GET",
                Uri = new Uri("http://localhost/"),
            };
        }

        private async Task<string> SeedObservationDate(string effective)
        {
            var observation = new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept { Text = "containment-test" },
                Effective = new FhirDateTime(effective),
                Meta = NewTagMeta(),
            };

            return await UpsertAsync(observation);
        }

        private async Task<string> SeedObservationPeriod(string start, string end)
        {
            var observation = new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept { Text = "containment-test" },
                Effective = new Period(new FhirDateTime(start), new FhirDateTime(end)),
                Meta = NewTagMeta(),
            };

            return await UpsertAsync(observation);
        }

        private async Task<string> SeedObservationForSubject(string patientId, string effective)
        {
            var observation = new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept { Text = "containment-test" },
                Effective = new FhirDateTime(effective),
                Subject = new ResourceReference($"Patient/{patientId}"),
                Meta = NewTagMeta(),
            };

            return await UpsertAsync(observation);
        }

        private async Task<string> SeedPatientBirthdate(string birthDate)
        {
            var patient = new Patient
            {
                BirthDate = birthDate,
                Meta = NewTagMeta(),
            };

            return await UpsertAsync(patient);
        }

        private Meta NewTagMeta() => new Meta { Tag = new List<Coding> { new Coding(TagSystem, _tag) } };

        private async Task<string> UpsertAsync(Resource resource)
        {
            if (string.IsNullOrEmpty(resource.Id))
            {
                resource.Id = Guid.NewGuid().ToString("N");
            }

            resource.Meta ??= new Meta();
            resource.Meta.LastUpdated = DateTimeOffset.UtcNow;

            ResourceElement resourceElement = resource.ToResourceElement();
            var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest("PUT");
            var compartmentIndices = Substitute.For<CompartmentIndices>();

            // Extract REAL search indices (date ranges, references, _tag tokens) so the SQL search can
            // actually match — bypassing the fixture's substitute indexer.
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(
                resourceElement,
                rawResource,
                resourceRequest,
                deleted: false,
                searchIndices,
                compartmentIndices,
                new List<KeyValuePair<string, string>>(),
                _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(resource.TypeName));
            wrapper.SearchParameterHash = "hash";

            await _scopedDataStore.Value.UpsertAsync(
                new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null),
                CancellationToken.None);

            return resource.Id;
        }

        private static async Task<FhirTypedElementToSearchValueConverterManager> CreateFhirTypedElementToSearchValueConverterManagerAsync()
        {
            var types = typeof(ITypedElementToSearchValueConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor(), new FhirServerInstanceConfiguration());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirElementToSearchValueConverters = new List<ITypedElementToSearchValueConverter>();

            foreach (Type type in types.Where(type => type.Name != nameof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter)))
            {
                // Filter out the extension converter because it will be added to the converter dictionary in the converter manager's constructor.
                var converter = (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, referenceSearchValueParser, codeSystemResolver);
                fhirElementToSearchValueConverters.Add(converter);
            }

            return new FhirTypedElementToSearchValueConverterManager(fhirElementToSearchValueConverters);
        }

        private static string Describe((string Name, string Value)[] query) =>
            string.Join("&", query.Select(q => $"{q.Name}={q.Value}"));

        private static void AssertSameSet(IReadOnlyCollection<string> expected, IReadOnlyCollection<string> actual, string because)
        {
            List<string> e = expected.OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> a = actual.OrderBy(x => x, StringComparer.Ordinal).ToList();
            Assert.True(
                e.SequenceEqual(a, StringComparer.Ordinal),
                $"{because}: expected [{string.Join(",", e)}] but got [{string.Join(",", a)}]");
        }
    }
}
