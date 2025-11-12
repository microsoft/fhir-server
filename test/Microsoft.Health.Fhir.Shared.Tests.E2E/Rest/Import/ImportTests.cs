// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportTests : IClassFixture<ImportTestFixture<StartupForImportTestProvider>>
    {
        private const string ForbiddenMessage = "Forbidden: Authorization failed.";

        private readonly TestFhirClient _client;
        private readonly MetricHandler _metricHandler;
        private readonly ImportTestFixture<StartupForImportTestProvider> _fixture;
        private static readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();
        private static readonly FhirJsonParser _fhirJsonParser = new FhirJsonParser();

        public ImportTests(ImportTestFixture<StartupForImportTestProvider> fixture)
        {
            _client = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
            _fixture = fixture;
        }

        [Fact]
        public async Task CheckNumberOfDequeues()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            ExecuteSql("INSERT INTO dbo.Parameters(Id, Char) SELECT 'DequeueJob', 'LogEvent'");

            await Task.Delay(TimeSpan.FromSeconds(12));

            var dequeues = (int)ExecuteSql(@$"
SELECT count(*)
  FROM dbo.EventLog 
  WHERE EventDate > dateadd(second,-10,getUTCdate())
    AND Process = 'DequeueJob'
    AND Mode LIKE 'Q=2 %' -- import
                ");

            // polling interval is set to 1 second. 2 jobs. expected 20.
            Assert.True(dequeues > 16 && dequeues < 24, $"not expected dequeues={dequeues}");
        }

        [Theory]
        [InlineData(false)] // eventualConsistency=false
        [InlineData(true)] // eventualConsistency=true
        public async Task GivenIncremental_WithExceptionOnDate_ImportShouldFail_AndResourcesShouldBeCommittedDependingOnConsistencyLevel(bool eventualConsistency)
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            ExecuteSql("IF object_id('DateTimeSearchParam_Trigger') IS NOT NULL DROP TRIGGER DateTimeSearchParam_Trigger");
            ExecuteSql("TRUNCATE TABLE dbo.JobQueue");
            ExecuteSql("TRUNCATE TABLE dbo.Transactions");

            try
            {
                ExecuteSql(@"
CREATE TRIGGER DateTimeSearchParam_Trigger ON DateTimeSearchParam FOR INSERT
AS
RAISERROR('TestError',18,127)
                ");

                var id = Guid.NewGuid().ToString("N");
                var ndJson = CreateTestPatient(id, birhDate: "2000-01-01");
                var request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson.ToString(), _fixture.StorageAccount)).location, ImportMode.IncrementalLoad, eventualConsistency: eventualConsistency);
                var checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
                var jobId = long.Parse(checkLocation.LocalPath.Split('/').Last());
                var message = await ImportWaitAsync(checkLocation, false);
                Assert.Equal(HttpStatusCode.InternalServerError, message.StatusCode);
                var result = (string)ExecuteSql($"SELECT Result FROM dbo.JobQueue WHERE QueueType = 2 AND Status = 3 AND GroupId = {jobId}");
                Assert.Contains("TestError", result); // job result should contain all details
                ExecuteSql("DROP TRIGGER DateTimeSearchParam_Trigger");

                // with eventual consistency we should be able to get resource by id
                var resourceSurrogateId = (long)ExecuteSql($"SELECT isnull((SELECT ResourceSurrogateId FROM dbo.Resource WHERE ResourceTypeId = 103 AND ResourceId = '{id}'),0)");
                if (eventualConsistency)
                {
                    Assert.True(resourceSurrogateId > 0);
                }
                else
                {
                    Assert.True(resourceSurrogateId == 0);
                    return;
                }

                // but not by date
                var cnt = (int)ExecuteSql($"SELECT count(*) FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = 103 AND ResourceSurrogateId = {resourceSurrogateId}");
                Assert.Equal(0, cnt);

                // Watchdog should update indexes in 63 seconds
                var sw = Stopwatch.StartNew();
                while ((int)ExecuteSql($"SELECT count(*) FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = 103 AND ResourceSurrogateId = {resourceSurrogateId}") == 0
                       && sw.Elapsed.TotalSeconds < 100)
                {
                    await Task.Delay(1000);
                }

                cnt = (int)ExecuteSql($"SELECT count(*) FROM dbo.DateTimeSearchParam WHERE ResourceTypeId = 103 AND ResourceSurrogateId = {resourceSurrogateId}");
                Assert.Equal(1, cnt);
            }
            finally
            {
                ExecuteSql("IF object_id('DateTimeSearchParam_Trigger') IS NOT NULL DROP TRIGGER DateTimeSearchParam_Trigger");
            }
        }

        [Fact]
        public async Task GivenIncrementalLoad_WithNotRetriableSqlExceptionForOrchestrator_ImportShouldFail()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            ExecuteSql("IF object_id('JobQueue_Trigger') IS NOT NULL DROP TRIGGER JobQueue_Trigger");
            try
            {
                var registration = await RegisterImport();
                ExecuteSql(@"
CREATE TRIGGER JobQueue_Trigger ON JobQueue FOR INSERT
AS
RAISERROR('TestError',18,127)
                ");
                var message = await ImportWaitAsync(registration.CheckLocation, false);
                Assert.Equal(HttpStatusCode.InternalServerError, message.StatusCode);
                Assert.True(!message.ReasonPhrase.Contains("TestError")); // message provided to customer should not contain internal details
                var result = (string)ExecuteSql($"SELECT Result FROM dbo.JobQueue WHERE QueueType = 2 AND Status = 3 AND JobId = {registration.JobId}");
                Assert.Contains("TestError", result); // job result should contain all details
            }
            finally
            {
                ExecuteSql("IF object_id('JobQueue_Trigger') IS NOT NULL DROP TRIGGER JobQueue_Trigger");
            }
        }

        [Fact]
        public async Task GivenIncrementalLoad_WithNotRetriableSqlExceptionForWorker_ImportShouldFail()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            ExecuteSql("IF object_id('Transactions_Trigger') IS NOT NULL DROP TRIGGER Transactions_Trigger");
            try
            {
                ExecuteSql(@"
CREATE TRIGGER Transactions_Trigger ON Transactions FOR UPDATE
AS
RAISERROR('TestError',18,127)
                ");
                var registration = await RegisterImport();
                var message = await ImportWaitAsync(registration.CheckLocation, false);
                Assert.Equal(HttpStatusCode.InternalServerError, message.StatusCode);
                Assert.True(!message.ReasonPhrase.Contains("TestError")); // message provided to customer should not contain internal details
                var result = (string)ExecuteSql($"SELECT Result FROM dbo.JobQueue WHERE QueueType = 2 AND Status = 3 AND GroupId = {registration.JobId} AND GroupId <> JobId");
                Assert.Contains("TestError", result); // job result should contain all details
            }
            finally
            {
                ExecuteSql("IF object_id('Transactions_Trigger') IS NOT NULL DROP TRIGGER Transactions_Trigger");
            }
        }

        [Theory]
        [InlineData(3)] // import should succeed
        [InlineData(6)] // import shoul fail
        public async Task GivenIncrementalLoad_WithExecutionTimeoutExceptionForWorker_ImportShouldReturnCorrectly(int requestedExceptions)
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            ExecuteSql("IF object_id('Transactions_Trigger') IS NOT NULL DROP TRIGGER Transactions_Trigger");
            try
            {
                ExecuteSql("TRUNCATE TABLE EventLog");
                ExecuteSql("TRUNCATE TABLE Transactions");
                ExecuteSql(@$"
CREATE TRIGGER Transactions_Trigger ON Transactions FOR UPDATE
AS
IF (SELECT count(*) FROM EventLog WHERE Process = 'MergeResourcesCommitTransaction' AND Status = 'Error') < {requestedExceptions} 
  RAISERROR('execution timeout expired',18,127)
                    ");
                var registration = await RegisterImport();
                var message = await ImportWaitAsync(registration.CheckLocation, false);
                Assert.Equal(requestedExceptions == 6 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK, message.StatusCode);
                var retries = (int)ExecuteSql("SELECT count(*) FROM EventLog WHERE Process = 'MergeResourcesCommitTransaction' AND Status = 'Error'");
                Assert.Equal(requestedExceptions == 6 ? 5 : 3, retries);
            }
            finally
            {
                ExecuteSql("IF object_id('Transactions_Trigger') IS NOT NULL DROP TRIGGER Transactions_Trigger");
            }
        }

        private object ExecuteSql(string sql)
        {
            using var conn = new SqlConnection(_fixture.ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            return cmd.ExecuteScalar();
        }

        private async Task<(Uri CheckLocation, long JobId)> RegisterImport(bool eventualConsistency = false)
        {
            var ndJson = PrepareResource(Guid.NewGuid().ToString("N"), null, null); // do not specify (version/last updated) to run without transaction
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            var checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            var id = long.Parse(checkLocation.LocalPath.Split('/').Last());
            return (checkLocation, id);
        }

        [Fact]
        public async Task GivenIncrementalLoad_1001ResourcesWithSameLastUpdatedAndSequenceRollOver()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            var st = DateTime.UtcNow;
            ExecuteSql("DELETE FROM Resource WHERE ResourceTypeId = 103 AND ResourceSurrogateId BETWEEN 4794128640080000000 AND 4794128640080000000 + 79999");
            ExecuteSql("ALTER SEQUENCE dbo.ResourceSurrogateIdUniquifierSequence RESTART WITH 0");
            ExecuteSql("IF 0 <> (SELECT current_value FROM sys.sequences WHERE name = 'ResourceSurrogateIdUniquifierSequence') RAISERROR('sequence is not at 0', 18, 127)");

            var ndJson = new StringBuilder();
            for (var i = 0; i < 1000; i++)
            {
                ndJson.Append(CreateTestPatient(Guid.NewGuid().ToString("N"), DateTimeOffset.Parse("1900-01-01Z00:00:01"))); // make sure this date is not used by other tests.
            }

            var request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson.ToString(), _fixture.StorageAccount)).location, ImportMode.IncrementalLoad);
            var checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            await ImportWaitAsync(checkLocation, true);

            ExecuteSql("IF 999 <> (SELECT current_value FROM sys.sequences WHERE name = 'ResourceSurrogateIdUniquifierSequence') RAISERROR('sequence is not at 999', 18, 127)");

            // similate load which rolls sequence over to 0
            ExecuteSql(@"
DECLARE @TransactionId bigint
EXECUTE dbo.MergeResourcesBeginTransaction 79000, @TransactionId OUT
EXECUTE dbo.MergeResourcesCommitTransaction @TransactionId
            ");

            ExecuteSql("IF 79999 <> (SELECT current_value FROM sys.sequences WHERE name = 'ResourceSurrogateIdUniquifierSequence') RAISERROR('sequence is not at 79999', 18, 127)");

            ndJson = new StringBuilder();
            for (var i = 0; i < 10; i++)
            {
                ndJson.Append(CreateTestPatient(Guid.NewGuid().ToString("N"), DateTimeOffset.Parse("1900-01-01Z00:00:01"))); // make sure this date is not used by other tests.
            }

            request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson.ToString(), _fixture.StorageAccount)).location, ImportMode.IncrementalLoad);
            checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            //// This import was failing after 30 retries before changing retries to be dependent on the batch size
            //// Now it suceedes after 100 retries
            await ImportWaitAsync(checkLocation, true);

            ExecuteSql($"IF 100 <> (SELECT count(*) FROM dbo.EventLog WHERE Process = 'MergeResources' AND Status = 'Error' AND EventText LIKE '%2627%' AND EventDate > '{st}') RAISERROR('Number of errors is not 100', 18, 127)");
        }

        [Fact]
        public async Task GivenIncrementalLoad_80KSurrogateIds_BadRequestIsReturned()
        {
            // To minimize number of size dependent retries last batch should have max size - 1000.
            // Create 81 files with 1000 resources each. This should also use all available processing threads.
            var locations = new List<Uri>();
            for (var l = 0; l < 81; l++)
            {
                locations.Add(await CreateNDJson(1000));
            }

            var request = CreateImportRequest(locations, ImportMode.IncrementalLoad);
            var checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            var message = await ImportWaitAsync(checkLocation, false);
            Assert.Equal(HttpStatusCode.BadRequest, message.StatusCode);
            Assert.Contains(ImportProcessingJob.SurrogateIdsErrorMessage, await message.Content.ReadAsStringAsync());
        }

        private async Task<Uri> CreateNDJson(int resources)
        {
            var strbld = new StringBuilder();
            for (var r = 0; r < resources; r++)
            {
                var str = CreateTestPatient(Guid.NewGuid().ToString("N"), DateTimeOffset.Parse("1900-01-01Z00:00")); // make sure this date is not used by other tests.));
                strbld.Append(str);
            }

            return (await ImportTestHelper.UploadFileAsync(strbld.ToString(), _fixture.StorageAccount)).location;
        }

        [Fact]
        public async Task ProcessingUnitBytesToReadHonored()
        {
            var ndJson = CreateTestPatient(Guid.NewGuid().ToString("N"));

            //// set small bytes to read, so there are multiple processing jobs
            var request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location, ImportMode.IncrementalLoad, processingUnitBytesToRead: 10);
            var result = await ImportCheckAsync(request, null, 0, true);
            Assert.Equal(7, result.Output.Count); // 7 processing jobs

            //// no details
            request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location, ImportMode.IncrementalLoad, processingUnitBytesToRead: 10);
            result = await ImportCheckAsync(request, null, 0, false);
            Assert.Single(result.Output);

            //// default bytes to read
            request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location, ImportMode.IncrementalLoad);
            result = await ImportCheckAsync(request, null, 0, true);
            Assert.Single(result.Output);

            //// 0 should be ovewritten by default in orchestrator job
            request = CreateImportRequest((await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location, ImportMode.IncrementalLoad, processingUnitBytesToRead: 0);
            result = await ImportCheckAsync(request, null, 0, true);
            Assert.Single(result.Output);
        }

        [Fact]
        public async Task GivenIncrementalLoad_TruncatedLastUpdatedPreservedWithOffset()
        {
            var id = Guid.NewGuid().ToString("N");
            var lastUpdated = new DateTimeOffset(DateTime.Parse(DateTime.Now.AddYears(-1).ToString()).AddSeconds(0.0001), TimeSpan.FromHours(10));
            var ndJson = CreateTestPatient(id, lastUpdated);
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false);
            await ImportCheckAsync(request, null, 0);

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Single(history.Resource.Entry);
            Assert.Equal("1", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal(lastUpdated.TruncateToMillisecond(), history.Resource.Entry[0].Resource.Meta.LastUpdated);

            var lastUpdatedUtc = new DateTimeOffset(lastUpdated.DateTime.AddHours(-10), TimeSpan.Zero);
            Assert.Equal(lastUpdated.UtcDateTime, lastUpdatedUtc.UtcDateTime); // the same date in UTC sense
            ndJson = CreateTestPatient(id, lastUpdatedUtc);
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false);
            await ImportCheckAsync(request, null, 0); // reported loaded

            // but was not inserted
            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Single(history.Resource.Entry);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleImportsWithSameLastUpdatedAndExplicitVersion()
        {
            var id = Guid.NewGuid().ToString("N");
            var baseDate = DateTimeOffset.UtcNow.AddYears(-1);
            var ndJson = CreateTestPatient(id, baseDate, "2");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Single(history.Resource.Entry);
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);

            // load prior version
            ndJson = CreateTestPatient(id, baseDate.AddYears(-1), "1");
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("1", history.Resource.Entry[1].Resource.VersionId);

            // re-load prior version
            ndJson = CreateTestPatient(id, baseDate.AddYears(-1), "1");
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("1", history.Resource.Entry[1].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleImportsWithSameLastUpdatedAndImplicitVersion()
        {
            var id = Guid.NewGuid().ToString("N");
            var baseDate = DateTimeOffset.UtcNow.AddYears(-1);
            var ndJson = CreateTestPatient(id, baseDate, "2");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Single(history.Resource.Entry);
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);

            // load prior version
            ndJson = CreateTestPatient(id, baseDate.AddYears(-1));
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("1", history.Resource.Entry[1].Resource.VersionId);

            // re-load prior version
            ndJson = CreateTestPatient(id, baseDate.AddYears(-1));
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("1", history.Resource.Entry[1].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_WithNegativeVersions_MultipleImports_ResourceExisting()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow.AddDays(-1), "4");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("4", result.Resource.Meta.VersionId);

            ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow, "-100"); // latest but negative
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            result = await _client.ReadAsync<Patient>(ResourceType.Patient, id); // negative is loaded as history
            Assert.Equal("4", result.Resource.Meta.VersionId);

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            Assert.Equal("-100", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("4", history.Resource.Entry[1].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_WithNegativeVersions_NegativeNotAllowed()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow, "-200");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false);
            await ImportCheckAsync(request, null, 1);
        }

        [Fact]
        public async Task GivenIncrementalLoad_WithNegativeVersions_MultipleImports_ResourceNotExisting()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow.AddDays(-2), "-200");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0); // loaded

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Single(history.Resource.Entry);
            Assert.Equal("-200", history.Resource.Entry[0].Resource.VersionId);

            ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow.AddDays(-4), "-100"); // out of order
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0); // loaded

            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            Assert.Equal("-200", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("-100", history.Resource.Entry[1].Resource.VersionId);

            ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow.AddDays(-1), "-5");
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0); // loaded

            history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(3, history.Resource.Entry.Count);
            Assert.Equal("-5", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("-200", history.Resource.Entry[1].Resource.VersionId);
            Assert.Equal("-100", history.Resource.Entry[2].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputsWithImplicitVersionsExplicitLastUpdatedBeforeImplicit()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson1 = CreateTestPatient(id);
            var ndJson2 = CreateTestPatient(id, DateTimeOffset.UtcNow.AddDays(-1));
            var location = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Single(history.Resource.Entry);
            Assert.Equal("1", history.Resource.Entry[0].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_LastUpdatedOnResourceCannotBeInTheFuture()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.UtcNow.AddSeconds(60)); // set value higher than 10 seconds tolerance
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            var result = await ImportCheckAsync(request, null, 1);
            var errorLocation = result.Error.ToArray()[0].Url;
            var errorContent = await ImportTestHelper.DownloadFileAsync(errorLocation, _fixture.StorageAccount);
            Assert.Contains("LastUpdated in the resource cannot be in the future.", errorContent);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputsWithImplicitVersionsExplicitLastUpdatedAfterImplicit()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson1 = CreateTestPatient(id);
            var ndJson2 = CreateTestPatient(id, DateTimeOffset.UtcNow.AddSeconds(8), birhDate: "1990"); // last updated within 10 seconds tolerance, different content
            var location = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);
            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
            //// same order
            Assert.True(int.Parse(history.Resource.Entry[0].Resource.VersionId) > int.Parse(history.Resource.Entry[1].Resource.VersionId));
            Assert.True(history.Resource.Entry[0].Resource.Meta.LastUpdated > history.Resource.Entry[1].Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersionsWithDelete()
        {
            var id = Guid.NewGuid().ToString("N");
            var date = DateTimeOffset.Parse("2000-01-01Z00:00");
            var ndJson1 = CreateTestPatient(id, date);
            var ndJson2 = CreateTestPatient(id, date.AddHours(1), deleted: true);
            var location = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);
            Assert.True(history.Resource.Entry[0].IsDeleted());
            Assert.Equal("1", history.Resource.Entry[1].Resource.VersionId);
            Assert.False(history.Resource.Entry[1].IsDeleted());
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrder2NotExplicit_ResourceExisting_WithGap()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson1 = PrepareResource(id, "1", "2001");
            var ndJson4 = PrepareResource(id, "10", "2004");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson4, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, null, "2003");
            location = (await ImportTestHelper.UploadFileAsync(ndJson2 + ndJson3, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("10", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2004"), result.Resource.Meta.LastUpdated);

            // check history by history search
            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal("10", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("9", history.Resource.Entry[1].Resource.VersionId);
            Assert.Equal("8", history.Resource.Entry[2].Resource.VersionId);
            Assert.Equal("1", history.Resource.Entry[3].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrder2NotExplicit_ResourceNotExisting_NoGap()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson1 = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, null, "2003");
            var ndJson4 = PrepareResource(id, "2", "2004");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson2 + ndJson3 + ndJson4, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2004"), result.Resource.Meta.LastUpdated);

            // check history by vread
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "-1");
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "-2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);

            // check history by history search
            var history = await _client.SearchAsync($"Patient/{id}/_history");
            Assert.Equal("2", history.Resource.Entry[0].Resource.VersionId);
            Assert.Equal("-1", history.Resource.Entry[1].Resource.VersionId);
            Assert.Equal("-2", history.Resource.Entry[2].Resource.VersionId);
            Assert.Equal("1", history.Resource.Entry[3].Resource.VersionId);
        }

        [Fact]
        public async Task GivenIncrementalLoad_InputVersionsOutOfSyncWithLastUpdated_SeparateImports()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "3", "2001");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 0);

            ndJson = PrepareResource(id, "2", "2002"); // 2<3 but 2002>2001
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 1);

            ndJson = PrepareResource(id, "4", "2000"); // 4>3 but 2000<2001
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 1);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_InputVersionsOutOfSyncWithLastUpdated_ResourceNotExisting()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "3", "2001");
            var ndJson2 = PrepareResource(id, "2", "2004");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson + ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request, null, 1);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2004"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrder1NotExplicit_ResourceNotExisting_NoGap_AllowNegative()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, "2", "2003");
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(ndJson + ndJson2 + ndJson3, _fixture.StorageAccount);
            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad, false, true);
            await ImportCheckAsync(request2, null, 0);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "-1");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrder1NotExplicit_ResourceNotExisting_NoGap_DoNotAllowNegative()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, "2", "2003");
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(ndJson + ndJson2 + ndJson3, _fixture.StorageAccount);
            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad, false, false);
            await ImportCheckAsync(request2, null, 1);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrder1NotExplicit_ResourceNotExisting()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, "3", "2003");
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(ndJson + ndJson2 + ndJson3, _fixture.StorageAccount);
            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request2, null);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleResoureTypesInSingleFile_Success()
        {
            var ndJson1 = Samples.GetNdJson("Import-SinglePatientTemplate");
            var pid = Guid.NewGuid().ToString("N");
            ndJson1 = ndJson1.Replace("##PatientID##", pid);
            var ndJson2 = Samples.GetNdJson("Import-Observation");
            var oid = Guid.NewGuid().ToString("N");
            ndJson2 = ndJson2.Replace("##ObservationID##", oid);
            var location = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false);
            await ImportCheckAsync(request, null);

            var patient = await _client.ReadAsync<Patient>(ResourceType.Patient, pid);
            Assert.Equal("1", patient.Resource.Meta.VersionId);
            var observation = await _client.ReadAsync<Observation>(ResourceType.Observation, oid);
            Assert.Equal("1", observation.Resource.Meta.VersionId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleNonSequentialInputVersions_ResourceExisting(bool setResourceType)
        {
            var id = Guid.NewGuid().ToString("N");

            // set existing
            var ndJson = PrepareResource(id, "10000", "2000");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, setResourceType);
            await ImportCheckAsync(request, null);

            // set input. something before and something after existing
            ndJson = PrepareResource(id, "9000", "1999");
            var ndJson2 = PrepareResource(id, "10100", "2001");
            var ndJson3 = PrepareResource(id, "10300", "2003");

            // note order of records
            location = (await ImportTestHelper.UploadFileAsync(ndJson2 + ndJson + ndJson3, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, setResourceType);
            await ImportCheckAsync(request, null);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("10300", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "9000");
            Assert.Equal(GetLastUpdated("1999"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "10100");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "10000");
            Assert.Equal(GetLastUpdated("2000"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_SameLastUpdated_DifferentVersions_ResourceExisting()
        {
            var id = Guid.NewGuid().ToString("N");

            var ndJson2 = PrepareResource(id, "2", "2002");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);

            // same date but different versions
            var ndJson1 = PrepareResource(id, "1", "2003");
            var ndJson3 = PrepareResource(id, "3", "2003");
            var location2 = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson3, _fixture.StorageAccount)).location;
            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request2, null, 1);

            result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_SameLastUpdated_DifferentVersions_DifferentImports_ResourceExisting()
        {
            var id = Guid.NewGuid().ToString("N");

            var ndJson2 = PrepareResource(id, "2", "2002");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);

            // same date but different versions
            var ndJson1 = PrepareResource(id, "1", "2003");
            location = (await ImportTestHelper.UploadFileAsync(ndJson1, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 1);
            var ndJson3 = PrepareResource(id, "3", "2003");
            location = (await ImportTestHelper.UploadFileAsync(ndJson3, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 0);

            result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_SameVersion_DifferentLastUpdated_ResourceExisting()
        {
            var id = Guid.NewGuid().ToString("N");

            var ndJson2 = PrepareResource(id, "2", "2002");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);

            // same version but different dates
            var ndJson1 = PrepareResource(id, "3", "2001");
            var ndJson3 = PrepareResource(id, "3", "2003");
            var location2 = (await ImportTestHelper.UploadFileAsync(ndJson1 + ndJson3, _fixture.StorageAccount)).location;
            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request2, null, 1);

            result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersions_ResourceExisting()
        {
            var id = Guid.NewGuid().ToString("N");

            // set existing
            var ndJson2 = PrepareResource(id, "2", "2002");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson2, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null);

            // set input
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson3 = PrepareResource(id, "3", "2003");
            var location2 = (await ImportTestHelper.UploadFileAsync(ndJson + ndJson2 + ndJson3, _fixture.StorageAccount)).location;
            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request2, null, 0);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Fact]
        public async Task GivenIncrementalLoad_MultipleInputVersions_ResourceNotExisting()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, "2", "2002");
            var ndJson3 = PrepareResource(id, "3", "2003");
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(ndJson + ndJson2 + ndJson3, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalImportInvalidResource_WhenImportData_ThenErrorLogsShouldBeOutputAndFailedCountShouldMatch(bool setResourceType)
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-InvalidPatient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var inputResource = new InputResource() { Url = location, Etag = etag };
            if (setResourceType)
            {
                inputResource.Type = "Patient";
            }

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>() { inputResource },
                Mode = ImportMode.IncrementalLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            var response = await ImportWaitAsync(checkLocation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(result.Output);
            Assert.Single(result.Error);
            Assert.NotEmpty(result.Request);

            string errorLocation = result.Error.ToArray()[0].Url;
            string[] errorContents = (await ImportTestHelper.DownloadFileAsync(errorLocation, _fixture.StorageAccount)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.True(errorContents.Count() >= 1); // when run locally there might be duplicates. no idea why.

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(1, notification.FailedCount);
                Assert.Equal(ImportMode.IncrementalLoad, notification.ImportMode);
            }
        }

        [Fact]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithoutImportPermissions_WhenImportData_ThenServerShouldReturnForbidden_WithNoImportNotification()
        {
            _metricHandler?.ResetCount();
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.ReadOnlyUser);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.IncrementalLoad.ToString(),
            };

            request.Mode = ImportMode.IncrementalLoad.ToString();
            request.Force = true;
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.ImportAsync(request.ToParameters(), CancellationToken.None));
            Assert.StartsWith(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                List<INotification> notificationList;
                _metricHandler.NotificationMapping.TryGetValue(typeof(ImportJobMetricsNotification), out notificationList);
                Assert.Null(notificationList);
            }
        }

        [Fact]
        public async Task GivenIncrementalLoad_WhenOutOfOrder_ThenCurrentDatabaseVersionShouldRemain()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "2", "2002");
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
            Assert.Equal("2", result.Resource.Meta.VersionId);

            ndJson = PrepareResource(id, "1", "2001");
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount);

            var request2 = CreateImportRequest(location2, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request2, null);

            result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated); // nothing changes on 2nd import
            Assert.Equal("2", result.Resource.Meta.VersionId);

            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated); // version 1 imported
        }

        [Fact]
        public async Task GivenIncrementalLoad_SameLastUpdated_SameVersion_DifferentContent_ShouldProduceConflict()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.Parse("2021-01-01Z00:00"), "2");

            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 0);

            ndJson = CreateTestPatient(id, DateTimeOffset.Parse("2021-01-01Z00:00"), "2", "2000");
            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 1);

            Assert.Single((await _client.SearchAsync($"Patient/{id}/_history")).Resource.Entry);
        }

        [Fact]
        public async Task GivenIncrementalLoad_SameLastUpdated_SameVersion_Run2Times_ShouldProduceSameResult()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.Parse("2021-01-01Z00:00"), "2");

            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 0);

            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 0);

            Assert.Single((await _client.SearchAsync($"Patient/{id}/_history")).Resource.Entry);
        }

        [Fact]
        public async Task GivenIncrementalLoad_SameLastUpdated_Run2Times_ShouldProduceSameResult()
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = CreateTestPatient(id, DateTimeOffset.Parse("2021-01-01Z00:00"));

            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 0);

            location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 0);

            Assert.Single((await _client.SearchAsync($"Patient/{id}/_history")).Resource.Entry);
        }

        [Fact]
        public async Task GivenIncrementalLoad_ThenInputLastUpdatedAndVersionShouldBeKept()
        {
            var id = Guid.NewGuid().ToString("N");
            var versionId = 2.ToString();
            var lastUpdatedYear = "2021";
            var lastUpdated = GetLastUpdated(lastUpdatedYear);
            var ndJson = PrepareResource(id, versionId, lastUpdatedYear);
            ndJson = ndJson + ndJson; // add one dup
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.IncrementalLoad);
            await ImportCheckAsync(request, null, 1);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.Equal(lastUpdated, result.Resource.Meta.LastUpdated);
            Assert.Equal(versionId, result.Resource.Meta.VersionId);
        }

        [Fact]
        public async Task GivenInitialLoad_ThenInputLastUpdatedAndVersionShouldNotBeKept()
        {
            var id = Guid.NewGuid().ToString("N");
            var versionId = 2.ToString();
            var lastUpdatedYear = "2021";
            var lastUpdated = GetLastUpdated(lastUpdatedYear);
            var ndJson = PrepareResource(id, versionId, lastUpdatedYear);
            ndJson = ndJson + ndJson; // add one dup
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.InitialLoad);
            await ImportCheckAsync(request, null, 1);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.NotEqual(lastUpdated, result.Resource.Meta.LastUpdated);
            Assert.NotEqual(versionId, result.Resource.Meta.VersionId);
        }

        private static DateTimeOffset GetLastUpdated(string lastUpdatedYear)
        {
            return DateTimeOffset.Parse(lastUpdatedYear + "-01-01T00:00:00.000+00:00");
        }

        private static ImportRequest CreateImportRequest(IList<Uri> locations, ImportMode importMode, bool setResourceType = true, bool allowNegativeVersions = false, string errorContainerName = null, bool eventualConsistency = false, int? processingUnitBytesToRead = null)
        {
            var input = locations.Select(location =>
            {
                var inputResource = new InputResource() { Url = location };
                if (setResourceType)
                {
                    inputResource.Type = "Patient";
                }

                return inputResource;
            }).ToList();

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = input,
                Mode = importMode.ToString(),
                AllowNegativeVersions = allowNegativeVersions,
                EventualConsistency = eventualConsistency,
                ErrorContainerName = errorContainerName,
            };

            if (processingUnitBytesToRead.HasValue)
            {
                request.ProcessingUnitBytesToRead = processingUnitBytesToRead.Value;
            }

            return request;
        }

        private static ImportRequest CreateImportRequest(Uri location, ImportMode importMode, bool setResourceType = true, bool allowNegativeVersions = false, string errorContainerName = null, bool eventualConsistency = false, int? processingUnitBytesToRead = null)
        {
            return CreateImportRequest([location], importMode, setResourceType, allowNegativeVersions, errorContainerName, eventualConsistency, processingUnitBytesToRead);
        }

        private static string PrepareResource(string id, string version, string lastUpdatedYear)
        {
            var ndJson = Samples.GetNdJson("Import-SinglePatientTemplate"); // "\"lastUpdated\":\"2020-01-01T00:00+00:00\"" "\"versionId\":\"1\"" "\"value\":\"654321\""
            ndJson = ndJson.Replace("##PatientID##", id);
            if (version != null)
            {
                ndJson = ndJson.Replace("\"versionId\":\"1\"", $"\"versionId\":\"{version}\"");
            }
            else
            {
                ndJson = ndJson.Replace("\"versionId\":\"1\",", string.Empty);
            }

            if (lastUpdatedYear != null)
            {
                ndJson = ndJson.Replace("\"lastUpdated\":\"2020-01-01T00:00:00.000+00:00\"", $"\"lastUpdated\":\"{lastUpdatedYear}-01-01T00:00:00.000+00:00\"");
            }
            else
            {
                ndJson = ndJson.Replace("\"lastUpdated\":\"2020-01-01T00:00:00.000+00:00\",", string.Empty);
            }

            return ndJson;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithImportPermissions_WhenImportData_TheServerShouldReturnSuccess(bool setResourceType)
        {
            _metricHandler?.ResetCount();
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.BulkImportUser);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.InitialLoad, setResourceType);
            await ImportCheckAsync(request, tempClient);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithoutImportPermissions_WhenImportData_ThenServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.ReadOnlyUser);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            request.Mode = ImportMode.InitialLoad.ToString();
            request.Force = true;
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.ImportAsync(request.ToParameters(), CancellationToken.None));
            Assert.StartsWith(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        public async Task GivenImportTriggered_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = etag,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportTriggered_WithAndWithoutETag_Then2ImportsShouldBeRegistered()
        {
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>() { new InputResource() { Url = location, Type = "Patient" } },
                Mode = ImportMode.IncrementalLoad.ToString(),
            };
            var checkLocation1 = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            var checkLocation2 = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            Assert.Equal(checkLocation1, checkLocation2); // idempotent registration
            await ImportWaitAsync(checkLocation1);

            request.Input = new List<InputResource>() { new InputResource() { Url = location, Type = "Patient", Etag = etag } };
            var checkLocation3 = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            Assert.NotEqual(checkLocation1, checkLocation3);
            await ImportWaitAsync(checkLocation3);
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportOperationTriggeredWithoutEtag_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.True(notificationList.Count() >= 1);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportResourceWithWrongType_ThenErrorLogShouldBeUploaded()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = etag,
                        Type = "Observation", // not match the resource
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            var response = await ImportWaitAsync(checkLocation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.Single(result.Error);
            Assert.NotEmpty(result.Error.First().Url);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(0, notification.SucceededCount);
                Assert.Equal(resourceCount, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportTriggeredWithMultipleFiles_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-SinglePatientTemplate");
            string resourceId1 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource1 = patientNdJsonResource.Replace("##PatientID##", resourceId1);
            string resourceId2 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource2 = patientNdJsonResource.Replace("##PatientID##", resourceId2);

            (Uri location1, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource1, _fixture.StorageAccount);
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource2, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location1,
                        Type = "Patient",
                    },
                    new InputResource()
                    {
                        Url = location2,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count * 2;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Equal(2, notificationList.Count);
                var succeeded = 0L;
                foreach (var notification in notificationList.Select(_ => (ImportJobMetricsNotification)_))
                {
                    Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                    Assert.NotNull(notification.DataSize);
                    succeeded += notification.SucceededCount.Value;
                    Assert.Equal(0, notification.FailedCount);
                }

                Assert.Equal(resourceCount, succeeded);
            }
        }

        [Fact]
        public async Task GivenImportInvalidResource_ThenErrorLogsShouldBeOutput()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-InvalidPatient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = etag,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            var response = await ImportWaitAsync(checkLocation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(result.Output);
            Assert.Single(result.Error);
            Assert.NotEmpty(result.Request);

            string errorLocation = result.Error.ToArray()[0].Url;
            string[] errorContents = (await ImportTestHelper.DownloadFileAsync(errorLocation, _fixture.StorageAccount)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.True(errorContents.Count() >= 1); // when run locally there might be duplicates. no idea why.

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(1, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportDuplicatedResource_ThenDupResourceShouldBeReported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-DupPatientTemplate");
            string resourceId = Guid.NewGuid().ToString("N");
            patientNdJsonResource = patientNdJsonResource.Replace("##PatientID##", resourceId);
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = etag,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            await ImportCheckAsync(request, errorCount: 1);
            //// we have to re-create file as import registration is idempotent
            (Uri location2, string etag2) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);
            request.Input = new List<InputResource>()
            {
                new InputResource()
                {
                    Url = location2,
                    Etag = etag2,
                    Type = "Patient",
                },
            };
            await ImportCheckAsync(request, errorCount: 2);

            Patient patient = await _client.ReadAsync<Patient>(ResourceType.Patient, resourceId);
            Assert.Equal(resourceId, patient.Id);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Equal(2, notificationList.Count);

                var notification1 = notificationList[0] as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification1.Status);
                Assert.Equal(1, notification1.SucceededCount);
                Assert.Equal(1, notification1.FailedCount);

                var notification2 = notificationList[1] as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification1.Status);
                Assert.Equal(0, notification2.SucceededCount);
                Assert.Equal(2, notification2.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportWithCancel_ThenTaskShouldBeCanceled()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = etag,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            // Then we cancel import job
            var response = await _client.CancelImport(checkLocation);

            // The service should accept the cancel request
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // We try to cancel the same job again, it should return Accepted
            response = await _client.CancelImport(checkLocation);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // We get the Import status
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.CheckImportAsync(checkLocation));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
            Assert.Contains("User requested cancellation", fhirException.Message);
        }

        [Fact]
        public async Task GivenImportHasCompleted_WhenCancel_ThenTaskShouldReturnConflict()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = etag,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            // Wait for import job to complete
            var importStatus = await _client.CheckImportAsync(checkLocation);

            // To avoid an infinite loop, we will try 5 times to get the completed status
            // Which we expect to finish because we are importing a single job
            for (int i = 0; i < 5; i++)
            {
                if (importStatus.StatusCode == HttpStatusCode.OK)
                {
                    break;
                }

                importStatus = await _client.CheckImportAsync(checkLocation);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            // Then we cancel import job
            var response = await _client.CancelImport(checkLocation);

            // The service should  return conflict because Import has already completed
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            // We try to cancel the same job again, it should return Conflict
            // We add this retry, in case customer send multiple cancel requests
            // We need to make sure the server returns Conflict
            response = await _client.CancelImport(checkLocation);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            // We get the Import status and it should return OK because Import completed
            importStatus = await _client.CheckImportAsync(checkLocation);
            Assert.Equal(HttpStatusCode.OK, importStatus.StatusCode);
        }

        [Fact(Skip = "long running tests for invalid url")]
        public async Task GivenImportOperationEnabled_WhenImportInvalidResourceUrl_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = new Uri("https://fhirtest-invalid.com"),
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () =>
                {
                    await ImportWaitAsync(checkLocation);
                });
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Failed.ToString(), notification.Status);
                Assert.Null(notification.DataSize);
                Assert.Null(notification.SucceededCount);
                Assert.Null(notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportInvalidETag_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Etag = "invalid",
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () =>
                {
                    await ImportWaitAsync(checkLocation);
                });
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Failed.ToString(), notification.Status);
                Assert.Equal(0, notification.DataSize);
                Assert.Equal(0, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportInvalidResourceType_ThenBadRequestShouldBeReturned()
        {
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Type = "Invalid",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () => await ImportTestHelper.CreateImportTaskAsync(_client, request));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
        }

        [Fact]
        public async Task GivenImportRequestWithMultipleSameFile_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-SinglePatientTemplate");
            string resourceId1 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource1 = patientNdJsonResource.Replace("##PatientID##", resourceId1);

            (Uri location1, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource1, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location1,
                        Type = "Patient",
                    },
                    new InputResource()
                    {
                        Url = location1,
                        Type = "Patient",
                    },
                },
                Mode = ImportMode.InitialLoad.ToString(),
            };

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () => await _client.ImportAsync(request.ToParameters()));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
        }

        [Fact]
        public async Task GivenImportRequest_WhenImportDataHasInvalidResourceIds_ResourcesShouldNotBeImported()
        {
            var errorContainerName = Guid.NewGuid().ToString("N").ToLower();
            var ids = new Dictionary<string, bool>
            {
                { "#badresourceid", false },
                { Guid.NewGuid().ToString("N"), true },
                { "/badresourceid/", false },
                { Guid.NewGuid().ToString("N"), true },
                { "bad#resource/id", false },
                { Guid.NewGuid().ToString(), true },
                { "?badresource&id", false },
                { "abc.123.ABC", true },
                { string.Empty, false },
                { Guid.NewGuid().ToString() + Guid.NewGuid().ToString(), false },
            };

            var ndjsons = new StringBuilder();
            foreach (var id in ids.Keys)
            {
                ndjsons.Append(PrepareResource(id, null, null));
            }

            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(
                ndjsons.ToString(),
                _fixture.StorageAccount);

            var request = CreateImportRequest(
                location: location,
                importMode: ImportMode.IncrementalLoad,
                errorContainerName: errorContainerName);
            var result = await ImportCheckAsync(request, null, ids.Values.Where(x => !x).Count());

            // Check if resources with the valid id are imported successfully.
            foreach (var pair in ids.Where(x => x.Value))
            {
                var readResponse = await _client.ReadAsync<Patient>(ResourceType.Patient, pair.Key);
                Assert.Equal(pair.Key, readResponse.Resource?.Id);
            }

            // Check errors having resources with the invalid id.
            var invalidIds = ids.Where(x => !x.Value).Select(x => x.Key).ToList();
            var errors = await ReadErrorsAsync(result.Error.ToArray()[0].Url);

            Assert.Equal(invalidIds.Count, errors.Count);
            invalidIds.ForEach(
                id =>
                {
                    var found = false;
                    foreach (var error in errors)
                    {
                        if (error.Issue.Where(x => x.Details?.Text?.Contains(id) ?? false).Any())
                        {
                            found = true;
                            break;
                        }
                    }

                    Assert.True(found, $"An error not found for resource id: '{id}'");
                });
        }

        private async Task<ImportJobResult> ImportCheckAsync(ImportRequest request, TestFhirClient client = null, int? errorCount = null, bool returnDetails = false)
        {
            client = client ?? _client;
            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(client, request);

            var response = await ImportWaitAsync(checkLocation, returnDetails: returnDetails);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(result.Output);
            if (errorCount != null && errorCount != 0)
            {
                Assert.Equal(errorCount.Value, result.Error.Count > 0 ? result.Error.First().Count : 0);
            }
            else
            {
                Assert.Empty(result.Error);
            }

            Assert.NotEmpty(result.Request);

            return result;
        }

        private async Task<HttpResponseMessage> ImportWaitAsync(Uri checkLocation, bool checkSuccessStatus = true, bool returnDetails = false)
        {
            HttpResponseMessage response;
            while ((response = await _client.CheckImportAsync(checkLocation, checkSuccessStatus, returnDetails)).StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            return response;
        }

        private string CreateTestPatient(string id = null, DateTimeOffset? lastUpdated = null, string versionId = null, string birhDate = null, bool deleted = false)
        {
            var rtn = new Patient()
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
                Meta = new(),
            };

            if (lastUpdated is not null)
            {
                rtn.Meta = new Meta { LastUpdated = lastUpdated };
            }

            if (versionId is not null)
            {
                rtn.Meta.VersionId = versionId;
            }

            if (birhDate != null)
            {
                rtn.BirthDate = birhDate;
            }

            if (deleted)
            {
                rtn.Meta.Extension = new List<Extension> { { new Extension(Core.Models.KnownFhirPaths.AzureSoftDeletedExtensionUrl, new FhirString("soft-deleted")) } };
            }

            return _fhirJsonSerializer.SerializeToString(rtn) + Environment.NewLine;
        }

        private async Task<List<OperationOutcome>> ReadErrorsAsync(string url)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(url);

            var resources = new List<OperationOutcome>();
            var content = await ImportTestHelper.DownloadFileAsync(url, _fixture.StorageAccount);
            if (!string.IsNullOrWhiteSpace(content))
            {
                using (var reader = new StringReader(content))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var resource = _fhirJsonParser.Parse<Hl7.Fhir.Model.Resource>(line);
                        if (resource?.TypeName?.Equals(nameof(OperationOutcome), StringComparison.OrdinalIgnoreCase) ?? false)
                        {
                            resources.Add((OperationOutcome)resource);
                        }
                    }
                }
            }

            return resources;
        }
    }
}
