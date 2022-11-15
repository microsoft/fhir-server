// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerExportTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly ISearchService _searchService;
        private readonly SqlServerFhirOperationDataStore _operationDataStore;
        private readonly SqlQueueClient _queueClient;
        private readonly ILoggerFactory _loggerFactory = new NullLoggerFactory();
        private readonly byte _queueType = (byte)QueueType.Export;

        public SqlServerExportTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
            _searchService = _fixture.GetService<ISearchService>();
            _operationDataStore = _fixture.GetService<SqlServerFhirOperationDataStore>();
            _queueClient = Substitute.ForPartsOf<SqlQueueClient>(_fixture.SqlConnectionWrapperFactory, _fixture.SchemaInformation, XUnitLogger<SqlQueueClient>.Create(_testOutputHelper));
        }

        [Fact]
        public async Task ExportWorkRegistration()
        {
            try
            {
                PrepareData(); // 1000 patients + 1000 observations + 1000 claims. !!! RawResource is invalid.

                var coordJob = new ExportOrchestratorJob(_queueClient, _searchService, _loggerFactory);
                coordJob.PollingIntervalSec = 0.3;
                coordJob.SurrogateIdRangeSize = 100;
                coordJob.NumberOfSurrogateIdRanges = 5;

                await RunExport("Patient", coordJob, 11, null); // 11=coord+1000/SurrogateIdRangeSize

                await RunExport("Patient,Observation", coordJob, 21, null); // 21=coord+2*1000/SurrogateIdRangeSize

                await RunExport(null, coordJob, 31, null); // 31=coord+3*1000/SurrogateIdRangeSize

                await RunExport(null, coordJob, 31, 6); // 31=coord+3*1000/SurrogateIdRangeSize 6=coord+100*5/SurrogateIdRangeSize
            }
            finally
            {
                ExecuteSql("DELETE FROM dbo.Resource");
            }
        }

        private async Task RunExport(string resourceType, ExportOrchestratorJob coordJob, int totalJobs, int? totalJobsAfterFailure)
        {
            var coordRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1);
            var result = await _operationDataStore.CreateExportJobAsync(coordRecord, CancellationToken.None);
            Assert.Equal(OperationStatus.Queued, result.JobRecord.Status);
            var coordId = long.Parse(result.JobRecord.Id);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            await RunCoordAndWorker(coordJob, coordId, groupId, totalJobs, totalJobsAfterFailure);
        }

        private async Task RunCoordAndWorker(ExportOrchestratorJob coordJob, long coordId, long groupId, int totalJobs, int? totalJobsAfterFailure)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(300));

            if (totalJobsAfterFailure.HasValue)
            {
                coordJob.RaiseTestException = true;
            }

            var coordRecord = JsonConvert.DeserializeObject<ExportJobRecord>((await _queueClient.DequeueAsync(_queueType, "Coord", 60, cts.Token, coordId)).Definition);
            var worker = Task.Factory.StartNew(() => Worker(cts.Token)); // must start after coord is dequeued

            retryOnTestException:
            try
            {
                await coordJob.ExecuteAsync(ToJobInfo(coordRecord, coordId, groupId), new Progress<string>(), cts.Token);
            }
            catch (ArgumentException e)
            {
                if (e.Message == "Test")
                {
                    var jobsAfterFailure = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
                    Assert.Equal(totalJobsAfterFailure.Value, jobsAfterFailure.Count);
                    coordJob.RaiseTestException = false;
                    goto retryOnTestException;
                }

                throw;
            }

            var jobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
            Assert.Equal(totalJobs, jobs.Count);
            cts.Cancel();
            try
            {
                await worker;
            }
            catch
            {
            }
        }

        private void Worker(CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                var job = _queueClient.DequeueAsync(_queueType, "Worker", 60, cancel).Result;
                if (job != null)
                {
                    job.Result = job.Definition;
                    _queueClient.CompleteJobAsync(job, false, cancel).Wait();
                }
                else
                {
                    Task.Delay(200, cancel).Wait(cancel);
                }
            }
        }

        private JobManagement.JobInfo ToJobInfo(ExportJobRecord record, long jobId, long groupId)
        {
            var jobInfo = new JobManagement.JobInfo();
            jobInfo.QueueType = _queueType;
            jobInfo.Id = jobId;
            jobInfo.GroupId = groupId;
            jobInfo.Definition = JsonConvert.SerializeObject(record);
            return jobInfo;
        }

        private void PrepareData()
        {
            ExecuteSql("DELETE FROM dbo.Resource");
            var surrId = _searchService.GetSurrogateId(DateTime.UtcNow);
            ExecuteSql(@$"
INSERT INTO Resource 
  SELECT ResourceTypeId
        ,newid()
        ,1
        ,0
        ,{surrId} - RowId * 1000 -- go to the past
        ,0
        ,null
        ,0x12345
        ,1
        ,null 
    FROM (SELECT RowId FROM (SELECT RowId = row_number() OVER (ORDER BY A1.id) FROM syscolumns A1, syscolumns A2) A WHERE RowId <= 1000) A
         CROSS JOIN (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name IN ('Patient','Observation','Claim')) B
                ");
        }

        private void ExecuteSql(string sql)
        {
            using var conn = new SqlConnection(_fixture.TestConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            cmd.CommandTimeout = 120;
            cmd.ExecuteNonQuery();
        }
    }
}
