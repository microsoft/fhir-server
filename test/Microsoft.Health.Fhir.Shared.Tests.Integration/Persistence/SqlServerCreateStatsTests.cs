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
        private readonly SqlServerSearchService _sqlSearchService;
        private readonly ITestOutputHelper _output;

        public SqlServerCreateStatsTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            _output = testOutputHelper;
        }

        [Fact]
        public async Task GivenImagingStudyWithIdentifier_StatsAreCreated()
        {
            using var conn = await _fixture.SqlHelper.GetSqlConnectionAsync();
            _output.WriteLine($"database={conn.Database}");

            const string resourceType = "ImagingStudy";
            var query = new[] { Tuple.Create("identifier", "xyz") };
            await _sqlSearchService.SearchAsync(resourceType, query, CancellationToken.None);
            var statsFromCache = SqlServerSearchService.GetStatsFromCache();
            foreach (var stat in statsFromCache)
            {
                _output.WriteLine($"cache {stat}");
            }

            foreach (var stat in await _sqlSearchService.GetStatsFromDatabase(CancellationToken.None))
            {
                _output.WriteLine($"database {stat}");
            }

            Assert.Single(statsFromCache.Where(_ => _.TableName == VLatest.TokenSearchParam.TableName
                                        && _.ColumnName == "Code"
                                        && _.ResourceTypeId == _sqlSearchService.Model.GetResourceTypeId(resourceType)
                                        && _.SearchParamId == _sqlSearchService.Model.GetSearchParamId(new Uri("http://hl7.org/fhir/SearchParameter/clinical-identifier"))));
        }

        ////[Fact]
        ////public async Task GivenPatientInCityWithCondition_StatsAreCreated()
        ////{
        ////    DropAllStats();
        ////    var query = new[] { Tuple.Create("address-city", "City"), Tuple.Create("_has:Condition:patient:code", "http://snomed.info/sct|444814009") };
        ////    await _sqlSearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
        ////    var stats = await _sqlSearchService.GetStats(CancellationToken.None);
        ////    Assert.NotNull(stats);
        ////    foreach (var stat in stats)
        ////    {
        ////        _testOutputHelper.WriteLine(stat.ToString());
        ////    }

        ////    Assert.Equal(2, stats.Count);
        ////}

        ////[Fact]
        ////public async Task GivenPatientInCityAndStateWithCondition_StatsAreCreated()
        ////{
        ////    DropAllStats();
        ////    var query = new[] { Tuple.Create("address-city", "City"), Tuple.Create("address-state", "State"), Tuple.Create("_has:Condition:patient:code", "http://snomed.info/sct|444814009") };
        ////    await _sqlSearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
        ////    var stats = await _sqlSearchService.GetStats(CancellationToken.None);
        ////    Assert.NotNull(stats);
        ////    foreach (var stat in stats)
        ////    {
        ////        _testOutputHelper.WriteLine(stat.ToString());
        ////    }

        ////    Assert.Equal(3, stats.Count);
        ////}

        ////[Fact]
        ////public async Task GivenPatientInCityAndStateAndBirthdateWithCondition_StatsAreCreated()
        ////{
        ////    DropAllStats();
        ////    var query = new[] { Tuple.Create("birthdate", "gt1800-01-01"), Tuple.Create("address-city", "City"), Tuple.Create("address-state", "State"), Tuple.Create("_has:Condition:patient:code", "http://snomed.info/sct|444814009") };
        ////    await _sqlSearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
        ////    var stats = await _sqlSearchService.GetStats(CancellationToken.None);
        ////    Assert.NotNull(stats);
        ////    foreach (var stat in stats)
        ////    {
        ////        _testOutputHelper.WriteLine(stat.ToString());
        ////    }

        ////    Assert.Equal(5, stats.Count); // +1 for end date
        ////}
    }
}
