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

            Assert.Single(statsFromCache.Where(
                _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == sqlSearchService.Model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-identifier"))));
        }

        [Fact]
        public async Task GivenSearchForResearchStudyByFocusAndDateWithResearchSubject_StatsAreCreated()
        {
            const string resourceType = "ResearchStudy";
            var query = new[] { Tuple.Create("focus", "surgery"), Tuple.Create("date", "gt1800-01-01"), Tuple.Create("_has:ResearchSubject:study:status", "eligible") };
            await _fixture.SearchService.SearchAsync(resourceType, query, CancellationToken.None);
            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            var model = ((SqlServerSearchService)_fixture.SearchService).Model;
            Assert.Single(statsFromCache.Where(
                _ => _.TableName == VLatest.TokenSearchParam.TableName
                  && _.ColumnName == "Code"
                  && _.ResourceTypeId == model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchStudy-focus"))));
            Assert.Single(statsFromCache.Where(
                _ => _.TableName == VLatest.DateTimeSearchParam.TableName
                  && _.ColumnName == "StartDateTime"
                  && _.ResourceTypeId == model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchStudy-date"))));
            Assert.Single(statsFromCache.Where(
                _ => _.TableName == VLatest.DateTimeSearchParam.TableName
                  && _.ColumnName == "EndDateTime"
                  && _.ResourceTypeId == model.GetResourceTypeId(resourceType)
                  && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchStudy-date"))));
            if (ModelInfoProvider.Instance.Version == FhirSpecification.R4)
            {
                Assert.Single(statsFromCache.Where(
                    _ => _.TableName == VLatest.TokenSearchParam.TableName
                      && _.ColumnName == "Code"
                      && _.ResourceTypeId == model.GetResourceTypeId("ResearchSubject")
                      && _.SearchParamId == model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/ResearchSubject-status"))));
            }
        }
    }
}
