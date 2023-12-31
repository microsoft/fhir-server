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
        private readonly ITestOutputHelper _testOutputHelper;

        public SqlServerCreateStatsTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _sqlSearchService = (SqlServerSearchService)_fixture.SearchService;
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task GivenPatientInCityWithCondition_StatsAreCreated()
        {
            DropAllStats();
            var query = new[] { Tuple.Create("address-city", "City"), Tuple.Create("_has:Condition:patient:code", "http://snomed.info/sct|444814009") };
            await _sqlSearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
            var stats = await _sqlSearchService.GetStats(CancellationToken.None);
            Assert.NotNull(stats);
            foreach (var stat in stats)
            {
                _testOutputHelper.WriteLine(stat.ToString());
            }

            Assert.Equal(2, stats.Count);
        }

        [Fact]
        public async Task GivenPatientInCityAndStateWithCondition_StatsAreCreated()
        {
            DropAllStats();
            var query = new[] { Tuple.Create("address-city", "City"), Tuple.Create("address-state", "State"), Tuple.Create("_has:Condition:patient:code", "http://snomed.info/sct|444814009") };
            await _sqlSearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
            var stats = await _sqlSearchService.GetStats(CancellationToken.None);
            Assert.NotNull(stats);
            foreach (var stat in stats)
            {
                _testOutputHelper.WriteLine(stat.ToString());
            }

            Assert.Equal(3, stats.Count);
        }

        [Fact]
        public async Task GivenPatientInCityAndStateAndBirthdateWithCondition_StatsAreCreated()
        {
            DropAllStats();
            var query = new[] { Tuple.Create("birthdate", "gt1800-01-01"), Tuple.Create("address-city", "City"), Tuple.Create("address-state", "State"), Tuple.Create("_has:Condition:patient:code", "http://snomed.info/sct|444814009") };
            await _sqlSearchService.SearchAsync(KnownResourceTypes.Patient, query, CancellationToken.None);
            var stats = await _sqlSearchService.GetStats(CancellationToken.None);
            Assert.NotNull(stats);
            foreach (var stat in stats)
            {
                _testOutputHelper.WriteLine(stat.ToString());
            }

            Assert.Equal(5, stats.Count); // +1 for end date
        }

        private void DropAllStats()
        {
            var stats = _sqlSearchService.GetStats(CancellationToken.None).Result;
            foreach (var stat in stats)
            {
                var sql = $"DROP STATISTICS {stat.TableName}.ST_{stat.ColumnName}_WHERE_ResourceTypeId_{stat.ResourceTypeId}_SearchParamId_{stat.SearchParamId}";
                _testOutputHelper.WriteLine(sql);
                ExecuteSql(sql);
            }

            stats = _sqlSearchService.GetStats(CancellationToken.None).Result;
            Assert.Empty(stats);
            SqlServerSearchService.ClearStatsCache();
        }

        private void ExecuteSql(string sql)
        {
            using var conn = _fixture.SqlHelper.GetSqlConnectionAsync().Result;
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.ExecuteNonQuery();
        }
    }
}
