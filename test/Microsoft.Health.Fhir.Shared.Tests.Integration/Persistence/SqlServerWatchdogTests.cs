// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Medino;
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
using Microsoft.SqlServer.Dac.Model;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable SA1116 // Split parameters should start on line after declaration

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

        [Fact]
        public async Task DefragBlocking()
        {
            // I don't know why blocking is not 100% reproduced. Hence this workaround.
            var retries = 0;
            while (true)
            {
                try
                {
                    await DefragBlockingMain();
                    break;
                }
                catch (Exception)
                {
                    if (retries++ > 3)
                    {
                        throw;
                    }
                }
            }
        }

        private async Task DefragBlockingMain()
        {
            ExecuteSql("EXECUTE dbo.DefragChangeDatabaseSettings 0");

            ExecuteSql("TRUNCATE TABLE dbo.EventLog");

            // enable logging
            ExecuteSql("INSERT INTO Parameters (Id,Char) SELECT name, 'LogEvent' FROM (SELECT name FROM sys.objects WHERE type = 'p' UNION ALL SELECT 'Search 'UNION ALL SELECT 'DefragBlocking') A");

            // populate data
            ExecuteSql(@"
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='Start',@Mode='',@Target='DefragBlockingTestTable',@Action='Create'
IF object_id('DefragBlockingTestTable') IS NOT NULL DROP TABLE dbo.DefragBlockingTestTable
CREATE TABLE dbo.DefragBlockingTestTable 
  (
    TypeId smallint
   ,Id int IDENTITY(1, 1)
   ,Data char(500) NOT NULL 
    
    CONSTRAINT PKC PRIMARY KEY CLUSTERED (TypeId, Id)
  )
INSERT INTO dbo.DefragBlockingTestTable (TypeId, Data) SELECT TOP 500000 96,'' FROM syscolumns A1, syscolumns A2
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='End',@Mode='',@Target='DefragBlockingTestTable',@Action='Insert',@Rows=@@rowcount
DELETE FROM dbo.DefragBlockingTestTable WHERE TypeId = 96 AND Id % 10 IN (0,1,2,3,4)
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='End',@Mode='',@Target='DefragBlockingTestTable',@Action='Delete',@Rows=@@rowcount
                ");

            // 4 tasks:
            // 1. Defrag starts and acquires schema stability lock
            // 2. Update stats. Starts after defrag start, tries to acquire schema modification lock, and is blocked by defrag, and waits.
            // 3. Query. Tries to acquire schema stability lock and is blocked by stats, and waits.
            // 4. Blocking monitor. Starts before everything. Looks for blocking. When bolocking duration exceeds threashold (1 sec), it kills stats.

            using var defragCancel = new CancellationTokenSource();
            var defrag = Task.Run(async () => await ExecuteSqlAsync(
                @"
DECLARE @st datetime = getUTCdate()
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='Start',@Mode='',@Target='DefragBlockingTestTable',@Action='Reorganize'
ALTER INDEX PKC ON dbo.DefragBlockingTestTable REORGANIZE
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='End',@Mode='',@Target='DefragBlockingTestTable',@Action='Reorganize',@Start=@st
                ",
                defragCancel.Token));

            var stats = Task.Run(async () => await ExecuteSqlAsync(
                @"
WAITFOR DELAY '00:00:01'
DECLARE @st datetime = getUTCdate()
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='Start',@Mode='',@Target='DefragBlockingTestTable',@Action='UpdateStats'
UPDATE STATISTICS dbo.DefragBlockingTestTable WITH FULLSCAN, ALL
EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='End',@Mode='',@Target='DefragBlockingTestTable',@Action='UpdateStats',@Start=@st
                ",
                CancellationToken.None));

            var queries = Task.Run(async () => await ExecuteSqlAsync(
                @"
DECLARE @i nvarchar(10) = 0, @st datetime, @SQL nvarchar(4000), @global datetime = getUTCdate()
WHILE datediff(second,@global,getUTCdate()) < 200 
      AND NOT EXISTS (SELECT * FROM dbo.EventLog WHERE Process = 'DefragBlocking' AND Action = 'UpdateStats' AND Status = 'End')
BEGIN
  SET @st = getUTCdate()
  EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='Start',@Mode=@i,@Target='DefragBlockingTestTable',@Action='Select'
  -- query should be in a separate batch from logging to correctly record start, hence sp_executeSQL
  SET @SQL = N'SELECT TOP 1000 TypeId, Id FROM dbo.DefragBlockingTestTable /* '+@i+' */ WHERE TypeId = 96 ORDER BY Id'
  EXECUTE sp_executeSQL @SQL
  EXECUTE dbo.LogEvent @Process='DefragBlocking',@Status='End',@Mode=@i,@Target='DefragBlockingTestTable',@Action='Select',@Start=@st,@Rows=@@rowcount
  SET @i = convert(int,@i)+1
  WAITFOR DELAY '00:00:01'
END
                ",
                CancellationToken.None));

            const int maxWait = 1000;
            var monitor = Task.Run(() => ExecuteSql(@"
DECLARE @st datetime = getUTCdate()
IF object_id('Sessions') IS NOT NULL DROP TABLE dbo.Sessions
SELECT Date = getUTCDate(), command, S.status, S.session_id, blocking_session_id, wait_type, wait_resource, wait_time, last_request_start_time
  INTO dbo.Sessions
  FROM sys.dm_exec_sessions S LEFT OUTER JOIN sys.dm_exec_requests R ON R.session_id = S.session_id
  WHERE 1 = 2
WHILE datediff(second,@st,getUTCdate()) < 200
      AND NOT EXISTS 
            (SELECT * 
               FROM dbo.Sessions 
               WHERE wait_type = 'LCK_M_SCH_S' 
                 AND command = 'SELECT'
                 AND wait_time > " + maxWait + @")
      AND NOT EXISTS 
            (SELECT * FROM dbo.EventLog WHERE Process = 'DefragBlocking' AND Action = 'UpdateStats' AND Status = 'End') 
BEGIN
  INSERT INTO dbo.Sessions
    SELECT Date = getUTCDate(), command, S.status, S.session_id, blocking_session_id, wait_type, wait_resource, wait_time, last_request_start_time
      FROM sys.dm_exec_sessions S LEFT OUTER JOIN sys.dm_exec_requests R ON R.session_id = S.session_id
      WHERE S.session_id <> @@spid
        AND S.status <> 'sleeping'
        AND wait_type <> 'WAITFOR'
  WAITFOR DELAY '00:00:01'
END
                "));

            await Task.WhenAll(monitor);
            defragCancel.Cancel();
            await Task.WhenAll(defrag);
            await Task.WhenAll(queries);
            await Task.WhenAll(stats);

            ExecuteSql("EXECUTE dbo.DefragChangeDatabaseSettings 1");

            Assert.True((int)ExecuteSql(@"
SELECT count(*) 
  FROM dbo.EventLog S
  WHERE S.Process = 'DefragBlocking' 
    AND S.Action = 'UpdateStats' 
    AND S.Status = 'Start'
    AND EXISTS (SELECT * 
                  FROM dbo.EventLog R
                  WHERE R.Process = 'DefragBlocking' 
                    AND R.Action = 'Reorganize'
                    AND R.Status = 'Start'
                    AND R.EventDate < S.EventDate
               ) 
            ") == 1,
            "incorrect execution sequence");
            var maxDuration = (int)ExecuteSql("SELECT max(Milliseconds) FROM dbo.EventLog WHERE Process = 'DefragBlocking' AND Action = 'Select' AND Status = 'End'");
            Assert.True(maxDuration > maxWait, $"low max duration = {maxDuration}");
            var minDuration = (int)ExecuteSql("SELECT min(Milliseconds) FROM dbo.EventLog WHERE Process = 'DefragBlocking' AND Action = 'Select' AND Status = 'End'");
            Assert.True(minDuration < 100, $"high min duration = {minDuration}");
            Assert.True((int)ExecuteSql("SELECT count(*) FROM dbo.EventLog WHERE Process = 'DefragBlocking' AND Action = 'Reorganize' AND Status = 'End'") == 0, "defrag not cancelled");
            Assert.True((int)ExecuteSql("SELECT count(*) FROM dbo.EventLog WHERE Process = 'DefragBlocking' AND Action = 'UpdateStats' AND Status = 'End'") == 1, "stats not completed");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Defrag(bool indexRebuildIsEnabled)
        {
            // enable logging
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
EXECUTE dbo.LogEvent @Process='Build',@Status='Warn',@Mode='',@Target='DefragTestTable',@Action='Delete',@Rows=@@rowcount
COMMIT TRANSACTION
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
            while ((GetEventLogCount("DatabaseStats.SearchParamCount") == 0) && (DateTime.UtcNow - startTime).TotalSeconds < 120)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.True((DateTime.UtcNow - startTime).TotalSeconds < 120, "DatabaseStats.SearchParamCount message is not found");

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
            cmd.CommandTimeout = 300;
            return cmd.ExecuteScalar();
        }

        private async Task ExecuteSqlAsync(string sql, CancellationToken cancel)
        {
            try
            {
                using var conn = new SqlConnection(_fixture.TestConnectionString);
                await conn.OpenAsync(cancel);
                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 300; // in PR test runs for >3.5 minutes
                await cmd.ExecuteScalarAsync(cancel);
            }
            catch (Exception e)
            {
                if (!e.ToString().Contains("cancel")
                    && !e.ToString().Contains("session is in the kill state"))
                {
                    throw;
                }
            }
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
