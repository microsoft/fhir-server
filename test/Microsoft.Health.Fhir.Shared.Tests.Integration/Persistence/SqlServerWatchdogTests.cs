﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Azure.Cosmos.Spatial;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerWatchdogTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;

        public SqlServerWatchdogTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Defrag(bool indexRebuildIsEnabled)
        {
            // ebale logging
            ExecuteSql("INSERT INTO Parameters (Id,Char) SELECT name, 'LogEvent' FROM (SELECT name FROM sys.objects WHERE type = 'p' UNION ALL SELECT 'Search') A");
            const string indexRebuildIsEnabledId = "Defrag.IndexRebuild.IsEnabled";
            ExecuteSql("DELETE FROM dbo.Parameters WHERE Id = '" + indexRebuildIsEnabledId + "'");
            if (indexRebuildIsEnabled)
            {
                ExecuteSql("INSERT INTO dbo.Parameters (Id, Number) SELECT Id = '" + indexRebuildIsEnabledId + "', 1");
            }

            // populate data
            ExecuteSql(@"
BEGIN TRANSACTION
IF object_id('DefragTestTable') IS NOT NULL DROP TABLE DefragTestTable
CREATE TABLE DefragTestTable (ResourceTypeId smallint, Id int IDENTITY(1, 1), Data char(500) NOT NULL PRIMARY KEY(Id, ResourceTypeId) ON PartitionScheme_ResourceTypeId (ResourceTypeId))
INSERT INTO DefragTestTable (ResourceTypeId, Data) SELECT TOP 100000 96,'' FROM syscolumns A1, syscolumns A2
DELETE FROM DefragTestTable WHERE ResourceTypeId = 96 AND Id % 10 IN (0,1,2,3,4,5,6,7,8)
COMMIT TRANSACTION
EXECUTE dbo.LogEvent @Process='Build',@Status='Warn',@Mode='',@Target='DefragTestTable',@Action='Delete',@Rows=@@rowcount
                ");

            await Task.Delay(TimeSpan.FromSeconds(10));

            var sizeBefore = GetSize();
            var current = GetDateTime();

            // Empty queue
            ExecuteSql("TRUNCATE TABLE dbo.JobQueue");

            var wd = new DefragWatchdog(
                _fixture.SqlRetryService,
                new SqlQueueClient(_fixture.SchemaInformation, _fixture.SqlRetryService, XUnitLogger<SqlQueueClient>.Create(_testOutputHelper)),
                XUnitLogger<DefragWatchdog>.Create(_testOutputHelper));

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            Task wsTask = wd.ExecuteAsync(cts.Token);

            DateTime startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");

            var completed = CheckQueue(current);
            while (!completed && (DateTime.UtcNow - startTime).TotalSeconds < 120)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
                completed = CheckQueue(current);
            }

            // make sure that exit from while was on queue check
            Assert.True(completed, "Work completed");

            var sizeAfter = GetSize();
            Assert.True(sizeAfter * 9 < sizeBefore, $"{sizeAfter} * 9 < {sizeBefore}");

            await cts.CancelAsync();
            await wsTask;
        }

        [Fact]
        public async Task CleanupEventLog()
        {
            // populate data
            ExecuteSql(@"
TRUNCATE TABLE dbo.EventLog
DECLARE @i int = 0
WHILE @i < 10500
BEGIN
  EXECUTE dbo.LogEvent @Process='Test',@Status='Warn',@Mode='Test'
  SET @i += 1
END
            ");

            _testOutputHelper.WriteLine($"EventLog.Count={GetCount("EventLog")}.");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            // TODO: Temp code to test database stats
            var factory = CreateResourceWrapperFactory();
            var tran = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesBeginTransactionAsync(1, cts.Token, DateTime.UtcNow.AddHours(-1)); // register timed out
            var patient = (Hl7.Fhir.Model.Patient)Samples.GetJsonSample("Patient").ToPoco();
            patient.Id = Guid.NewGuid().ToString();
            var wrapper = factory.Create(patient.ToResourceElement(), false, true);
            wrapper.ResourceSurrogateId = tran.TransactionId;
            var mergeWrapper = new MergeResourceWrapper(wrapper, true, true);
            await _fixture.SqlServerFhirDataStore.MergeResourcesWrapperAsync(tran.TransactionId, false, [mergeWrapper], false, 0, cts.Token);
            var typeId = _fixture.SqlServerFhirModel.GetResourceTypeId("Patient");
            ExecuteSql($"IF NOT EXISTS (SELECT * FROM dbo.Resource WHERE ResourceTypeId = {typeId} AND ResourceId = '{patient.Id}') RAISERROR('Resource is not created',18,127)");

            var wd = new CleanupEventLogWatchdog(_fixture.SqlRetryService, XUnitLogger<CleanupEventLogWatchdog>.Create(_testOutputHelper));

            Task wdTask = wd.ExecuteAsync(cts.Token);

            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            startTime = DateTime.UtcNow;
            while ((GetCount("EventLog") > 2000) && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            _testOutputHelper.WriteLine($"EventLog.Count={GetCount("EventLog")}.");
            Assert.True(GetCount("EventLog") <= 2000, "Count is high");

            // TODO: Temp code to test database stats
            startTime = DateTime.UtcNow;
            while ((GetEventLogCount("tmp_GetRawResources") == 0) && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.True((DateTime.UtcNow - startTime).TotalSeconds < 60, "tmp_GetRawResources message is not found");

            startTime = DateTime.UtcNow;
            while ((GetEventLogCount("DatabaseStats.ResourceTypeTotals") == 0) && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.True((DateTime.UtcNow - startTime).TotalSeconds < 60, "DatabaseStats.ResourceTypeTotals message is not found");

            startTime = DateTime.UtcNow;
            while ((GetEventLogCount("DatabaseStats.SearchParamCount") == 0) && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.True((DateTime.UtcNow - startTime).TotalSeconds < 60, "DatabaseStats.SearchParamCount message is not found");

            await cts.CancelAsync();
            await wdTask;
        }

        [Fact]
        public async Task RollTransactionForward()
        {
            ExecuteSql("TRUNCATE TABLE dbo.Transactions");
            ExecuteSql("TRUNCATE TABLE dbo.Resource");
            ExecuteSql("TRUNCATE TABLE dbo.NumberSearchParam");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            var factory = CreateResourceWrapperFactory();

            var tran = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesBeginTransactionAsync(1, cts.Token, DateTime.UtcNow.AddHours(-1)); // register timed out

            var patient = (Hl7.Fhir.Model.Patient)Samples.GetJsonSample("Patient").ToPoco();
            patient.Id = Guid.NewGuid().ToString();
            var wrapper = factory.Create(patient.ToResourceElement(), false, true);
            wrapper.ResourceSurrogateId = tran.TransactionId;
            var mergeWrapper = new MergeResourceWrapper(wrapper, true, true);

            ExecuteSql(@"
CREATE TRIGGER dbo.tmp_NumberSearchParam ON dbo.NumberSearchParam FOR INSERT
AS
RAISERROR('Test',18,127)
            ");

            try
            {
                // no eventual consistency
                await _fixture.SqlServerFhirDataStore.MergeResourcesWrapperAsync(tran.TransactionId, true, [mergeWrapper], false, 0, cts.Token);
            }
            catch (SqlException e)
            {
                Assert.Equal("Test", e.Message);
            }

            Assert.Equal(0, GetCount("Resource")); // resource not inserted

            try
            {
                // eventual consistency
                await _fixture.SqlServerFhirDataStore.MergeResourcesWrapperAsync(tran.TransactionId, false, [mergeWrapper], false, 0, cts.Token);
            }
            catch (SqlException e)
            {
                Assert.Equal("Test", e.Message);
            }

            Assert.Equal(1, GetCount("Resource")); // resource inserted
            Assert.Equal(0, GetCount("NumberSearchParam")); // number is not inserted

            ExecuteSql("DROP TRIGGER dbo.tmp_NumberSearchParam");

            var wd = new TransactionWatchdog(_fixture.SqlServerFhirDataStore, factory, _fixture.SqlRetryService, XUnitLogger<TransactionWatchdog>.Create(_testOutputHelper))
            {
                AllowRebalance = true,
                PeriodSec = 1,
                LeasePeriodSec = 2,
            };

            Task wdTask = wd.ExecuteAsync(cts.Token);
            DateTime startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 20)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            startTime = DateTime.UtcNow;
            while (GetCount("NumberSearchParam") == 0 && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.Equal(1, GetCount("NumberSearchParam")); // wd rolled forward transaction

            await cts.CancelAsync();
            await wdTask;
        }

        [Fact]
        public async Task AdvanceVisibility()
        {
            ExecuteSql("TRUNCATE TABLE dbo.Transactions");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var wd = new TransactionWatchdog(_fixture.SqlServerFhirDataStore, CreateResourceWrapperFactory(), _fixture.SqlRetryService, XUnitLogger<TransactionWatchdog>.Create(_testOutputHelper))
            {
                AllowRebalance = true,
                PeriodSec = 1,
                LeasePeriodSec = 2,
            };

            Task wdTask = wd.ExecuteAsync(cts.Token);
            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 20)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // create 3 trans
            var tran1 = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesBeginTransactionAsync(1, cts.Token, DateTime.UtcNow.AddHours(1));
            var tran2 = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesBeginTransactionAsync(1, cts.Token, DateTime.UtcNow.AddHours(1));
            var tran3 = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesBeginTransactionAsync(1, cts.Token, DateTime.UtcNow.AddHours(1));
            var visibility = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesGetTransactionVisibilityAsync(cts.Token);
            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(-1, visibility);

            // commit 1
            await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesCommitTransactionAsync(tran1.TransactionId, null, cts.Token);
            _testOutputHelper.WriteLine($"Tran1={tran1.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesGetTransactionVisibilityAsync(cts.Token)) != tran1.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1), cts.Token);
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran1.TransactionId, visibility);

            // commit 3
            await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesCommitTransactionAsync(tran3.TransactionId, null, cts.Token);
            _testOutputHelper.WriteLine($"Tran3={tran3.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesGetTransactionVisibilityAsync(cts.Token)) != tran2.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1), cts.Token);
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran1.TransactionId, visibility); // remains t1 though t3 is committed.

            // commit 2
            await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesCommitTransactionAsync(tran2.TransactionId, null, cts.Token);
            _testOutputHelper.WriteLine($"Tran2={tran2.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesGetTransactionVisibilityAsync(cts.Token)) != tran3.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1), cts.Token);
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran3.TransactionId, visibility);

            await cts.CancelAsync();
            await wdTask;
        }

        [Fact]
        public async Task GeoReplicationLagWatchdog()
        {
            // Enable logging
            ExecuteSql("INSERT INTO Parameters (Id,Char) SELECT name, 'LogEvent' FROM (SELECT name FROM sys.objects WHERE type = 'p' UNION ALL SELECT 'GeoReplicationLagWatchdog') A WHERE name NOT IN (SELECT Id FROM Parameters WHERE Char = 'LogEvent')");

            // Verify the stored procedure exists
            var spExists = (int)ExecuteSql("SELECT COUNT(*) FROM sys.objects WHERE name = 'GetGeoReplicationLag' AND type = 'P'");
            Assert.Equal(1, spExists);
            _testOutputHelper.WriteLine("GetGeoReplicationLag stored procedure exists");

            // Initialize watchdog parameters
            var geoWatchdog = new GeoReplicationLagWatchdog();
            ExecuteSql($"DELETE FROM dbo.Parameters WHERE Id IN ('{geoWatchdog.PeriodSecId}', '{geoWatchdog.LeasePeriodSecId}')");
            ExecuteSql($"INSERT INTO dbo.Parameters (Id, Number) VALUES ('{geoWatchdog.PeriodSecId}', 1)");
            ExecuteSql($"INSERT INTO dbo.Parameters (Id, Number) VALUES ('{geoWatchdog.LeasePeriodSecId}', 2)");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            var schemaInformation = _fixture.SchemaInformation;

            var wd = new GeoReplicationLagWatchdog(
                _fixture.SqlRetryService,
                XUnitLogger<GeoReplicationLagWatchdog>.Create(_testOutputHelper),
                Substitute.For<IMediator>(),
                schemaInformation)
            {
                AllowRebalance = true,
                PeriodSec = 1,
                LeasePeriodSec = 2,
            };

            Task wdTask = wd.ExecuteAsync(cts.Token);

            DateTime startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.5), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // Wait for at least one execution cycle - the watchdog should handle the SqlException gracefully
            await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

            await cts.CancelAsync();
            await wdTask;

            _testOutputHelper.WriteLine("Watchdog executed successfully");
        }

        private ResourceWrapperFactory CreateResourceWrapperFactory()
        {
            var serializer = new FhirJsonSerializer();
            var rawResourceFactory = new RawResourceFactory(serializer);
            var dummyRequestContext = new FhirRequestContext(
                "POST",
                "https://localhost/Patient",
                "https://localhost/",
                Guid.NewGuid().ToString(),
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>());
            var fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            fhirRequestContextAccessor.RequestContext.Returns(dummyRequestContext);

            var searchIndexer = Substitute.For<ISearchIndexer>();
            searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new List<SearchIndexEntry>() { new SearchIndexEntry(new SearchParameterInfo("param", "param", SearchParamType.Number, new Uri("http://hl7.org/fhir/SearchParameter/Immunization-lot-number")), new NumberSearchValue(1000)) });

            var searchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            searchParameterDefinitionManager.GetSearchParameterHashForResourceType(Arg.Any<string>()).Returns("hash");

            return new ResourceWrapperFactory(
                rawResourceFactory,
                fhirRequestContextAccessor,
                searchIndexer,
                Substitute.For<IClaimsExtractor>(),
                Substitute.For<ICompartmentIndexer>(),
                searchParameterDefinitionManager,
                Deserializers.ResourceDeserializer);
        }

        private object ExecuteSql(string sql)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 120;
            return cmd.ExecuteScalar();
        }

        private long GetCount(string table)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand($"SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id('{table}') AND index_id IN (0,1)", conn);
            var res = cmd.ExecuteScalar();
            return (long)res;
        }

        private long GetEventLogCount(string process)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand($"SELECT count(*) FROM dbo.EventLog WHERE Process = '{process}'", conn);
            var res = cmd.ExecuteScalar();
            return (int)res;
        }

        private double GetSize()
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT convert(float,sum(used_page_count)*8.0/1024/1024) FROM sys.dm_db_partition_stats WHERE object_id = object_id('DefragTestTable')", conn);
            var res = cmd.ExecuteScalar();
            return (double)res;
        }

        private DateTime GetDateTime()
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT getUTCdate()", conn);
            var res = cmd.ExecuteScalar();
            return (DateTime)res;
        }

        private bool CheckQueue(DateTime current)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT TOP 1000 Definition, Status FROM dbo.JobQueue WHERE QueueType = @QueueType AND CreateDate > @Current ORDER BY JobId DESC", conn);
            cmd.Parameters.AddWithValue("@QueueType", Core.Features.Operations.QueueType.Defrag);
            cmd.Parameters.AddWithValue("@Current", current);
            using SqlDataReader reader = cmd.ExecuteReader();
            var coordCompleted = false;
            var coordArchived = false;
            var workArchived = false;
            while (reader.Read())
            {
                var def = reader.GetString(0);
                var status = reader.GetByte(1);
                if (string.Equals(def, "Defrag", StringComparison.OrdinalIgnoreCase) && status == 2)
                {
                    coordCompleted = true;
                }

                if (string.Equals(def, "Defrag", StringComparison.OrdinalIgnoreCase) && status == 5)
                {
                    coordArchived = true;
                }

                if (def.Contains("DefragTestTable", StringComparison.OrdinalIgnoreCase) && status == 5)
                {
                    workArchived = true;
                }
            }

            return coordCompleted && coordArchived && workArchived;
        }
    }
}
