// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests validating SQL Server search statistics creation when SMART on FHIR V2
    /// fine-grained (granular) scopes with search parameter filters are applied.
    /// </summary>
    /// <remarks>
    /// These tests ensure:
    /// 1. Search executions constrained by granular SMART scopes generate token/string statistics
    ///    entries for only the permitted resource types and filtered search parameters.
    /// 2. _include and wildcard _include queries respect scope restrictions (e.g., referenced
    ///    resources without explicit scope are not returned) while still producing stats for
    ///    the search parameters actually exercised.
    /// 3. The in-memory cache and persisted database statistics contain entries for
    ///    each search parameter used directly (query parameters) or indirectly (scope filters).
    /// </remarks>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerCreateStatsForSmartTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public SqlServerCreateStatsForSmartTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _output = testOutputHelper;

            _contextAccessor = fixture.FhirRequestContextAccessor;
        }

        [Fact]
        public async Task GivenSmartV2GranularScopeWithSearchParamSearchForObservation_StatsAreCreated()
        {
            /*
             * Test validates that _include respects scope restrictions with granular scopes
             * scopes = patient/Observation.s?code=http://loinc.org|4548-4 patient/Practitioner.s?gender=female (Observation scope with specific code and female Practitioners ONLY, no Patient scope)
             * Since there is no Patient scope, included Patients should NOT be returned (secure behavior)
             * There are no female practitioners. Only observations with code 4548-4 should be returned, WITHOUT included Patient resources
             */
            var scopeRestriction1 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4")));
            var scopeRestriction2 = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("gender", "female")));
            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { scopeRestriction1, scopeRestriction2 }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Include with Observation:subject
            // Search for Observation resources with _include=Observation:subject (tries to include Patient)
            // Should return ONLY smart-observation-A1 (which has code 4548-4), NOT the Patient (no Patient scope)
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_include", "Observation:subject"));
            query.Add(new Tuple<string, string>("identifier", "test"));
            await _fixture.SearchService.SearchAsync("Observation", query, CancellationToken.None);

            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            foreach (var stat in statsFromCache)
            {
                _output.WriteLine($"cache {stat}");
            }

            var sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            foreach (var stat in await sqlSearchService.GetStatsFromDatabase(CancellationToken.None))
            {
                _output.WriteLine($"database {stat}");
            }

            // Assert for Observation with clinical-code
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-code")));

            // Observation with identifier (from direct query parameter)
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-identifier")));
        }

        [Fact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingObservationsWithWildcardInclude_StatsAreCreated()
        {
            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and wildcard include
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Practitioner.s?name=SmartPract&gender=female
             * Search: Observation?_include=*
             * Expected: Observation with code 55233-1 AND status=final, Patient with name=SMARTGivenName1 AND gender=male
             *          No Practitioners should be returned (no female practitioner exists matching the scope)
             */
            var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));
            var practitionerScope = new ScopeRestriction("Practitioner", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SmartPract"), ("gender", "female")));

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, patientScope, practitionerScope }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Observations with wildcard include
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_include", "*"));
            await _fixture.SearchService.SearchAsync("Observation", query, CancellationToken.None);

            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            foreach (var stat in statsFromCache)
            {
                _output.WriteLine($"cache {stat}");
            }

            var sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            var dbStat = await sqlSearchService.GetStatsFromDatabase(CancellationToken.None);
            foreach (var stat in dbStat)
            {
                _output.WriteLine($"database {stat}");
            }

            // Assert for Observation with clinical-code
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-code")));

            // Assert for Observation with clinical-code
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Observation-status")));

            // Patient with gender
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Patient")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/individual-gender")));

            // Patient with name
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.StringSearchParam.TableName
                  && _.ColumnName == "Text"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Patient")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")));

            // Practitioner with gender
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Practitioner")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/individual-gender")));

            // Practitioner with name
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.StringSearchParam.TableName
                  && _.ColumnName == "Text"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Practitioner")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Practitioner-name")));
        }

        [Fact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingPatientsWithMultipleRevIncludes_StatsAreCreated()
        {
            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and specific revincludes
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Encounter.s?status=finished&class=IMP
             * Search: Patient?_revinclude=Observation:subject&_revinclude=Encounter:subject
             * Expected: Patient with name=SMARTGivenName1 AND gender=male,
             *          Observation with code 55233-1 AND status=final that references the patient,
             *          No Encounters should be returned if none match status=finished AND class=IMP
             */
            var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            var observationScope2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-9")));
            var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));

            // Using Encounter with restrictive filters to potentially exclude encounters
            var encounterScope = new ScopeRestriction("Encounter", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "finished")));

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, observationScope2, patientScope, encounterScope }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient with specific revincludes for Observation and Encounter
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_revinclude", "Observation:subject"));
            query.Add(new Tuple<string, string>("_revinclude", "Encounter:subject"));
            await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            foreach (var stat in statsFromCache)
            {
                _output.WriteLine($"cache {stat}");
            }

            var sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            var dbStat = await sqlSearchService.GetStatsFromDatabase(CancellationToken.None);
            foreach (var stat in dbStat)
            {
                _output.WriteLine($"database {stat}");
            }

            // Assert for Observation with clinical-code
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-code")));

            // Assert for Observation with clinical-code
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Observation-status")));

            // Patient with gender
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Patient")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/individual-gender")));

            // Patient with name
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.StringSearchParam.TableName
                  && _.ColumnName == "Text"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Patient")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")));

            // Encounter with status
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Encounter")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Encounter-status")));
        }

        [Fact]
        public async Task GivenSmartV2MultipleGranularScopesWithSpecificFilters_WhenSearchingPatientsWithSingleRevIncludes_StatsAreCreated()
        {
            /*
             * Test validates complex scenario with multiple granular scopes with specific filters and specific revincludes
             * scopes = patient/Observation.s?code=http://loinc.org|55233-1&status=final
             *          patient/Patient.s?name=SMARTGivenName1&gender=male
             *          patient/Encounter.s?status=finished&class=IMP
             * Search: Patient?_revinclude=Observation:subject&_revinclude=Encounter:subject
             * Expected: Patient with name=SMARTGivenName1 AND gender=male,
             *          Observation with code 55233-1 AND status=final that references the patient,
             *          No Encounters should be returned if none match status=finished AND class=IMP
             */
            var observationScope = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-4"), ("status", "final")));
            var observationScope2 = new ScopeRestriction("Observation", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("code", "http://loinc.org|4548-9")));
            var patientScope = new ScopeRestriction("Patient", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("name", "SMARTGivenName1"), ("gender", "male")));

            // Using Encounter with restrictive filters to potentially exclude encounters
            var encounterScope = new ScopeRestriction("Encounter", Core.Features.Security.DataActions.Search, "patient", CreateSearchParams(("status", "finished")));

            ConfigureFhirRequestContext(_contextAccessor, new List<ScopeRestriction>() { observationScope, observationScope2, patientScope, encounterScope }, true);
            _contextAccessor.RequestContext.AccessControlContext.CompartmentId = "smart-patient-A";
            _contextAccessor.RequestContext.AccessControlContext.CompartmentResourceType = "Patient";

            // Search for Patient with specific revincludes for Observation and Encounter
            var query = new List<Tuple<string, string>>();
            query.Add(new Tuple<string, string>("_revinclude", "Encounter:subject"));
            await _fixture.SearchService.SearchAsync("Patient", query, CancellationToken.None);

            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            foreach (var stat in statsFromCache)
            {
                _output.WriteLine($"cache {stat}");
            }

            var sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            var dbStat = await sqlSearchService.GetStatsFromDatabase(CancellationToken.None);
            foreach (var stat in dbStat)
            {
                _output.WriteLine($"database {stat}");
            }

            // Encounter with status
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Encounter")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Encounter-status")));

            // Patient with gender
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Patient")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/individual-gender")));

            // Patient with name
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.StringSearchParam.TableName
                  && _.ColumnName == "Text"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Patient")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")));
        }

        private void ConfigureFhirRequestContext(
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ICollection<ScopeRestriction> scopes,
            bool applyFineGrainedAccessControlWithSearchParameters = false)
        {
            var accessControlContext = new AccessControlContext()
            {
                ApplyFineGrainedAccessControl = true,
                ApplyFineGrainedAccessControlWithSearchParameters = applyFineGrainedAccessControlWithSearchParameters,
            };

            foreach (var scope in scopes)
            {
                accessControlContext.AllowedResourceActions.Add(scope);
            }

            contextAccessor.RequestContext.AccessControlContext.Returns(accessControlContext);
        }

        private static SearchParams CreateSearchParams(params (string key, string value)[] items)
        {
            var searchParams = new SearchParams();
            foreach (var item in items)
            {
                searchParams.Add(item.key, item.value);
            }

            return searchParams;
        }
    }
}
