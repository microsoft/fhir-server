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
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
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

            var queueClient = Substitute.ForPartsOf<SqlQueueClient>(_fixture.SqlConnectionWrapperFactory, _fixture.SchemaInformation, _fixture.SqlRetryService, XUnitLogger<SqlQueueClient>.Create(_testOutputHelper));
            var wd = new DefragWatchdog(
                () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(),
                () => queueClient.CreateMockScope(),
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

            _testOutputHelper.WriteLine($"EventLog.Count={GetCount()}.");

            var wd = new CleanupEventLogWatchdog(
                () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(),
                XUnitLogger<CleanupEventLogWatchdog>.Create(_testOutputHelper));

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

            while ((GetCount() > 1000) && (DateTime.UtcNow - startTime).TotalSeconds < 120)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            _testOutputHelper.WriteLine($"EventLog.Count={GetCount()}.");
            Assert.True(GetCount() <= 1000, "Count is low");

            wd.Dispose();
        }

        [Fact]
        public async Task RollTransactionForward()
        {
            var wd = new TransactionWatchdog(_fixture.SqlServerFhirDataStore, null, () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(), XUnitLogger<TransactionWatchdog>.Create(_testOutputHelper));

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            await wd.StartAsync(true, 1, 2, cts.Token);

            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            var patient = (Hl7.Fhir.Model.Patient)Samples.GetJsonSample("Patient").ToPoco();
            patient.Id = Guid.NewGuid().ToString();
            ////await _fixture.Med.UpsertResourceAsync(patient.ToResourceElement());
            ////var a = _fixture.SearchParameterDefinitionManager;

            var tran = await _fixture.SqlServerFhirDataStore.MergeResourcesBeginTransactionAsync(1, CancellationToken.None);

            wd.Dispose();
        }

        [Fact]
        public async Task AdvanceVisibility()
        {
            var wd = new TransactionWatchdog(_fixture.SqlServerFhirDataStore, GetResourceWrapperFactory(), () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(), XUnitLogger<TransactionWatchdog>.Create(_testOutputHelper));

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            await wd.StartAsync(true, 1, 2, cts.Token);

            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // create 3 trans
            var tran1 = await _fixture.SqlServerFhirDataStore.MergeResourcesBeginTransactionAsync(1, CancellationToken.None);
            var tran2 = await _fixture.SqlServerFhirDataStore.MergeResourcesBeginTransactionAsync(1, CancellationToken.None);
            var tran3 = await _fixture.SqlServerFhirDataStore.MergeResourcesBeginTransactionAsync(1, CancellationToken.None);
            var visibility = await _fixture.SqlServerFhirDataStore.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None);
            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(-1, visibility);

            // commit 1
            await _fixture.SqlServerFhirDataStore.MergeResourcesCommitTransactionAsync(tran1.TransactionId, null, CancellationToken.None);
            _testOutputHelper.WriteLine($"Tran1={tran1.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None)) != tran1.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran1.TransactionId, visibility);

            // commit 3
            await _fixture.SqlServerFhirDataStore.MergeResourcesCommitTransactionAsync(tran3.TransactionId, null, CancellationToken.None);
            _testOutputHelper.WriteLine($"Tran3={tran3.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None)) != tran2.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran1.TransactionId, visibility); // remains t1 though t3 is committed.

            // commit 2
            await _fixture.SqlServerFhirDataStore.MergeResourcesCommitTransactionAsync(tran2.TransactionId, null, CancellationToken.None);
            _testOutputHelper.WriteLine($"Tran2={tran2.TransactionId} committed.");

            startTime = DateTime.UtcNow;
            while ((visibility = await _fixture.SqlServerFhirDataStore.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None)) != tran3.TransactionId && (DateTime.UtcNow - startTime).TotalSeconds < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
            }

            _testOutputHelper.WriteLine($"Visibility={visibility}");
            Assert.Equal(tran3.TransactionId, visibility);

            wd.Dispose();
        }

        private ResourceWrapperFactory GetResourceWrapperFactory()
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

            var claimsExtractor = Substitute.For<IClaimsExtractor>();
            var compartmentIndexer = Substitute.For<ICompartmentIndexer>();
            var searchIndexer = Substitute.For<ISearchIndexer>();

            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.GetSearchParameterHashForResourceType(Arg.Any<string>()).Returns("hash");

            return new ResourceWrapperFactory(
                rawResourceFactory,
                fhirRequestContextAccessor,
                searchIndexer,
                claimsExtractor,
                compartmentIndexer,
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

        private long GetCount()
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT sum(row_count) FROM sys.dm_db_partition_stats WHERE object_id = object_id('EventLog')", conn);
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
