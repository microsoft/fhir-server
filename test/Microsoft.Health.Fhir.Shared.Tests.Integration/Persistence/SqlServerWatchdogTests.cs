// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
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
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
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

        [Fact]
        public async Task Defrag()
        {
            // populate data
            ExecuteSql(@"
BEGIN TRANSACTION
CREATE TABLE DefragTestTable (Id int IDENTITY(1, 1), Data char(500) NOT NULL PRIMARY KEY(Id))
INSERT INTO DefragTestTable (Data) SELECT TOP 100000 '' FROM syscolumns A1, syscolumns A2
DELETE FROM DefragTestTable WHERE Id % 10 IN (0,1,2,3,4,5,6,7,8)
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

            await wd.StartAsync(cts.Token);

            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");

            var completed = CheckQueue(current);
            while (!completed && (DateTime.UtcNow - startTime).TotalSeconds < 120)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                completed = CheckQueue(current);
            }

            // make sure that exit from while was on queue check
            Assert.True(completed, "Work completed");

            var sizeAfter = GetSize();
            Assert.True(sizeAfter * 9 < sizeBefore, $"{sizeAfter} * 9 < {sizeBefore}");

            wd.Dispose();
        }

        [Fact]
        public async Task CleanupEventLog()
        {
            // populate data
            ExecuteSql(@"
TRUNCATE TABLE dbo.EventLog
DECLARE @i int = 0
WHILE @i < 10000
BEGIN
  EXECUTE dbo.LogEvent @Process='Test',@Status='Warn',@Mode='Test'
  SET @i += 1
END
                ");

            _testOutputHelper.WriteLine($"EventLog.Count={GetCount("EventLog")}.");

            var wd = new CleanupEventLogWatchdog(_fixture.SqlRetryService, XUnitLogger<CleanupEventLogWatchdog>.Create(_testOutputHelper));

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            await wd.StartAsync(cts.Token);

            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            while ((GetCount("EventLog") > 1000) && (DateTime.UtcNow - startTime).TotalSeconds < 120)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            _testOutputHelper.WriteLine($"EventLog.Count={GetCount("EventLog")}.");
            Assert.True(GetCount("EventLog") <= 1000, "Count is low");

            wd.Dispose();
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

            ExecuteSql("INSERT INTO Parameters (Id,Number) SELECT 'MergeResources.NoTransaction.IsEnabled', 1");

            ExecuteSql(@"
CREATE TRIGGER dbo.tmp_NumberSearchParam ON dbo.NumberSearchParam
FOR INSERT
AS
BEGIN
  RAISERROR('Test',18,127)
  RETURN
END
            ");

            try
            {
                await _fixture.SqlServerFhirDataStore.MergeResourcesWrapperAsync(tran.TransactionId, false, new[] { mergeWrapper }, false, 0, cts.Token);
            }
            catch (SqlException e)
            {
                Assert.Equal("Test", e.Message);
            }

            Assert.Equal(1, GetCount("Resource")); // resource inserted
            Assert.Equal(0, GetCount("NumberSearchParam")); // number is not inserted

            ExecuteSql("DROP TRIGGER dbo.tmp_NumberSearchParam");

            var wd = new TransactionWatchdog(_fixture.SqlServerFhirDataStore, factory, _fixture.SqlRetryService, XUnitLogger<TransactionWatchdog>.Create(_testOutputHelper));
            await wd.StartAsync(true, 1, 2, cts.Token);
            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            startTime = DateTime.UtcNow;
            while (GetCount("NumberSearchParam") == 0 && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.Equal(1, GetCount("NumberSearchParam")); // wd rolled forward transaction

            wd.Dispose();
        }

        [Fact]
        public async Task AdvanceVisibility()
        {
            ExecuteSql("TRUNCATE TABLE dbo.Transactions");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var wd = new TransactionWatchdog(_fixture.SqlServerFhirDataStore, CreateResourceWrapperFactory(), _fixture.SqlRetryService, XUnitLogger<TransactionWatchdog>.Create(_testOutputHelper));
            await wd.StartAsync(true, 1, 2, cts.Token);
            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
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
                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran1.TransactionId, visibility);

            // commit 3
            await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesCommitTransactionAsync(tran3.TransactionId, null, cts.Token);
            _testOutputHelper.WriteLine($"Tran3={tran3.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesGetTransactionVisibilityAsync(cts.Token)) != tran2.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran1.TransactionId, visibility); // remains t1 though t3 is committed.

            // commit 2
            await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesCommitTransactionAsync(tran2.TransactionId, null, cts.Token);
            _testOutputHelper.WriteLine($"Tran2={tran2.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.StoreClient.MergeResourcesGetTransactionVisibilityAsync(cts.Token)) != tran3.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran3.TransactionId, visibility);

            wd.Dispose();
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

        private void ExecuteSql(string sql)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 120;
            cmd.ExecuteNonQuery();
        }

        private long GetCount(string table)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand($"SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id('{table}') AND index_id IN (0,1)", conn);
            var res = cmd.ExecuteScalar();
            return (long)res;
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
            using var cmd = new SqlCommand("SELECT TOP 10 Definition, Status FROM dbo.JobQueue WHERE QueueType = @QueueType AND CreateDate > @Current ORDER BY JobId DESC", conn);
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
