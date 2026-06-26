// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Microsoft.SqlServer.Dac.Model;
using Microsoft.VisualStudio.TestPlatform.Utilities;
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

        public SqlServerCreateStatsTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _output = testOutputHelper;
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

        [Fact]
        public async Task GivenSearchByReferenceParam_NormalPath_StatsAreCreated()
        {
            // Arrange — DiagnosticReport.subject is a reference search parameter stored in dbo.ReferenceSearchParam
            const string resourceType = "DiagnosticReport";
            const string searchParamUrl = "http://hl7.org/fhir/SearchParameter/DiagnosticReport-subject";
            var query = new[] { Tuple.Create("subject", "Patient/test-patient-1") };
            var sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            short resourceTypeId = sqlSearchService.Model.GetResourceTypeId(resourceType);
            short searchParamId = sqlSearchService.Model.GetSearchParamId(new Uri(searchParamUrl));
            short patientTypeId = sqlSearchService.Model.GetResourceTypeId("Patient");

            // Capture stats before the search so we can assert on the delta.
            var cacheBefore = SqlServerSearchService.GetStatsFromCache().ToList();
            var databaseBefore = (await sqlSearchService.GetStatsFromDatabase(CancellationToken.None)).ToList();

            // Act
            await _fixture.SearchService.SearchAsync(resourceType, query, CancellationToken.None);

            // Assert — a filtered stat on ReferenceResourceId with ReferenceResourceTypeId in the filter
            var cacheAfter = SqlServerSearchService.GetStatsFromCache().ToList();
            var databaseAfter = (await sqlSearchService.GetStatsFromDatabase(CancellationToken.None)).ToList();

            bool MatchesStat((string TableName, string ColumnName, short ResourceTypeId, short SearchParamId, short? ReferenceResourceTypeId) s) =>
                s.TableName == VLatest.ReferenceSearchParam.TableName
                && s.ColumnName == VLatest.ReferenceSearchParam.ReferenceResourceId.Metadata.Name
                && s.ResourceTypeId == resourceTypeId
                && s.SearchParamId == searchParamId
                && s.ReferenceResourceTypeId == patientTypeId;

            // Cache
            Assert.True(
                cacheAfter.Count(MatchesStat) > cacheBefore.Count(MatchesStat),
                "Expected the search to add a filtered statistics entry in the cache for ReferenceResourceId with ReferenceResourceTypeId filter.");

            // Database
            Assert.True(
                databaseAfter.Count(MatchesStat) > databaseBefore.Count(MatchesStat),
                "Expected the search to add a filtered statistics entry in the database for ReferenceResourceId with ReferenceResourceTypeId filter.");
        }

        [Fact]
        public async Task GivenSearchByReferenceParam_NotExistsPath_NoReferenceStatsAreCreated()
        {
            // Arrange — searching with :missing=true produces a NotExists table expression;
            // the stats pipeline must NOT emit entries for that expression kind.
            const string resourceType = "DiagnosticReport";
            var query = new[] { Tuple.Create("subject:missing", "true") };

            // Record the cache before the search so we can detect any new entries added only by this query.
            var statsBefore = SqlServerSearchService.GetStatsFromCache().ToList();

            // Act
            await _fixture.SearchService.SearchAsync(resourceType, query, CancellationToken.None);

            var statsAfter = SqlServerSearchService.GetStatsFromCache().ToList();

            // Assert — a NotExists expression must never produce ReferenceSearchParam stats.
            var newReferenceStats = statsAfter
                .Where(s => s.TableName == VLatest.ReferenceSearchParam.TableName
                    && !statsBefore.Any(b =>
                        b.TableName == s.TableName
                        && b.ColumnName == s.ColumnName
                        && b.ResourceTypeId == s.ResourceTypeId
                        && b.SearchParamId == s.SearchParamId
                        && b.ReferenceResourceTypeId == s.ReferenceResourceTypeId))
                .ToList();

            Assert.Empty(newReferenceStats);
        }

        [Fact]
        public async Task GivenMissingTrueSearchForPatientByGender_NotExistsStatsAreCreated()
        {
            // :missing=true translates to a SQL NotExists table expression. The owning search parameter
            // (gender) must resolve to the single concrete resource type carried by the predicate (Patient)
            // and must NOT fan out a stat for every base resource type of the parameter.
            const string resourceType = "Patient";
            var query = new[] { Tuple.Create("gender:missing", "true") };
            await _fixture.SearchService.SearchAsync(resourceType, query, CancellationToken.None);

            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            foreach (var stat in statsFromCache)
            {
                _output.WriteLine($"cache {stat}");
            }

            var model = ((SqlServerSearchService)_fixture.SearchService).Model;
            var genderParamId = model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/individual-gender"));
            var patientResourceTypeId = model.GetResourceTypeId(resourceType);

            // A value-column stat on the owning parameter column is created for exactly one (concrete) resource type.
            Assert.Single(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == patientResourceTypeId
                  && _.SearchParamId == genderParamId);

            // The BaseResourceTypes fallback is intentionally not used for NotExists, so the gender stat is
            // never created for any resource type other than the one in the search URL.
            Assert.DoesNotContain(statsFromCache, _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId != patientResourceTypeId
                  && _.SearchParamId == genderParamId);
        }
    }
}
