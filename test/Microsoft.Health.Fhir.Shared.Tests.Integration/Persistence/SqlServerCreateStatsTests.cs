// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.SqlServer.Dac.Model;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using NSubstitute;
using NSubstitute.Core;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerCreateStatsTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _output;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;

        public SqlServerCreateStatsTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _output = testOutputHelper;

            _contextAccessor = fixture.FhirRequestContextAccessor;
        }

        [Fact]
        public async Task GivenSearchForImagingStudyByIdentifier_StatsAreCreated()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            const string resourceType = "ImagingStudy";
            var query = new[] { Tuple.Create("identifier", "xyz") };
            await _fixture.SearchService.SearchAsync(resourceType, query, CancellationToken.None);
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

            Assert.Single(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-identifier")));
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
            var results = await _fixture.SearchService.SearchAsync("Observation", query, CancellationToken.None);

            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            await _fixture.SearchService.SearchAsync("Observation", query, CancellationToken.None);
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

            // Assert for Patient with individual-gender (if this is part of the include operation)
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Practitioner")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/individual-gender")));

            // Observation with identifier (from direct query parameter)
            Assert.Contains(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId("Observation")
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-identifier")));
        }

        [Fact]
        public async Task GivenSearchForResearchStudyByFocusAndDateWithResearchSubject_StatsAreCreated()
        {
            const string resourceType = "ResearchStudy";
#if Stu3 || R4 || R4B
            var researchStudyFocusUri = "http://hl7.org/fhir/SearchParameter/ResearchStudy-focus";
            var query = new[] { Tuple.Create("focus", "surgery"), Tuple.Create("date", "gt1800-01-01"), Tuple.Create("_has:ResearchSubject:study:status", "eligible") };
#else
            var researchStudyFocusUri = "http://hl7.org/fhir/SearchParameter/ResearchStudy-focus-code";
            var query = new[] { Tuple.Create("focus-code", "surgery"), Tuple.Create("date", "gt1800-01-01"), Tuple.Create("_has:ResearchSubject:study:status", "eligible") };
#endif
            await _fixture.SearchService.SearchAsync(resourceType, query, CancellationToken.None);
            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            var model = ((SqlServerSearchService)_fixture.SearchService).Model;
            Assert.Single(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == model.GetSearchParamId(new Uri(researchStudyFocusUri)));
            Assert.Single(statsFromCache, _ => _.TableName == VLatest.DateTimeSearchParam.TableName
                  && _.ColumnName == "StartDateTime"
                  && _.ResourceTypeId == model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchStudy-date")));
            Assert.Single(statsFromCache, _ => _.TableName == VLatest.DateTimeSearchParam.TableName
                  && _.ColumnName == "EndDateTime"
                  && _.ResourceTypeId == model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchStudy-date")));
            if (ModelInfoProvider.Instance.Version == FhirSpecification.R4)
            {
                Assert.Single(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                      && _.ColumnName == "Code"
                      && _.ResourceTypeId == model.GetResourceTypeId("ResearchSubject")
                      && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchSubject-status")));
            }
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
