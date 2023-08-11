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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Export;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
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
        private readonly byte _queueType = (byte)QueueType.Export;
        private const string DropTrigger = "IF object_id('tmp_JobQueueIns') IS NOT NULL DROP TRIGGER tmp_JobQueueIns";

        public SqlServerExportTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
            _searchService = _fixture.GetService<ISearchService>();
            _operationDataStore = _fixture.GetService<SqlServerFhirOperationDataStore>();
            _queueClient = new SqlQueueClient(_fixture.SchemaInformation, _fixture.SqlRetryService, XUnitLogger<SqlQueueClient>.Create(_testOutputHelper));
        }

        [Fact]
        public async Task ExportWorkRegistration()
        {
            try
            {
                PrepareData(); // 1000 patients + 1000 observations + 1000 claims. !!! RawResource is invalid.

                var coordJob = new SqlExportOrchestratorJob(_queueClient, _searchService);
                //// surrogate id range size is set via max number of resources per query on coord record
                coordJob.NumberOfSurrogateIdRanges = 5; // 100*5=500 is 50% of 1000, so there are 2 insert transactions in JobQueue per each resource type

                await RunExport(null, coordJob, 31, 6); // 31=coord+3*1000/SurrogateIdRangeSize 6=coord+100*5/SurrogateIdRangeSize

                await RunExportWithCancel("Patient", coordJob, 11, null); // 11=coord+1000/SurrogateIdRangeSize

                await RunExport("Patient,Observation", coordJob, 21, null); // 21=coord+2*1000/SurrogateIdRangeSize

                await RunExport(null, coordJob, 31, null); // 31=coord+3*1000/SurrogateIdRangeSize
            }
            finally
            {
                ExecuteSql("TRUNCATE TABLE dbo.JobQueue");
                ExecuteSql("DELETE FROM dbo.Resource");
                ExecuteSql(DropTrigger);
            }
        }

        private async Task RunExportWithCancel(string resourceType, SqlExportOrchestratorJob coordJob, int totalJobs, int? totalJobsAfterFailure)
        {
            var coorId = await RunExport(resourceType, coordJob, totalJobs, totalJobsAfterFailure);
            var record = (await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None)).JobRecord;
            Assert.Equal(OperationStatus.Running, record.Status);
            record.Status = OperationStatus.Canceled;
            var result = await _operationDataStore.UpdateExportJobAsync(record, null, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);
            result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);
        }

        private async Task<string> RunExport(string resourceType, SqlExportOrchestratorJob coordJob, int totalJobs, int? totalJobsAfterFailure)
        {
            var coordRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1, maximumNumberOfResourcesPerQuery: 100);
            var result = await _operationDataStore.CreateExportJobAsync(coordRecord, CancellationToken.None);
            Assert.Equal(OperationStatus.Queued, result.JobRecord.Status);
            var coordId = long.Parse(result.JobRecord.Id);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            await RunCoordinator(coordJob, coordId, groupId, totalJobs, totalJobsAfterFailure);
            return coordId.ToString();
        }

        private async Task RunCoordinator(SqlExportOrchestratorJob coordJob, long coordId, long groupId, int totalJobs, int? totalJobsAfterFailure)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(300));

            if (totalJobsAfterFailure.HasValue)
            {
                ExecuteSql(DropTrigger);
                ExecuteSql(@"
CREATE TRIGGER dbo.tmp_JobQueueIns ON dbo.JobQueue
FOR INSERT
AS
BEGIN
  IF (SELECT count(*) FROM dbo.JobQueue) > 10 RAISERROR('Count > 10',18,127)
  RETURN
END
                    ");
            }

            var jobInfo = await _queueClient.DequeueAsync(_queueType, "Coord", 60, cts.Token, coordId);

            retryOnTestException:
            try
            {
                await coordJob.ExecuteAsync(jobInfo, new Progress<string>(), cts.Token);
                await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Count > 10"))
                {
                    var jobsAfterFailure = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
                    Assert.Equal(totalJobsAfterFailure.Value, jobsAfterFailure.Count);
                    ExecuteSql(DropTrigger);
                    goto retryOnTestException;
                }

                throw;
            }

            var jobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
            Assert.Equal(totalJobs, jobs.Count);
        }

        private void PrepareData()
        {
            ExecuteSql("TRUNCATE TABLE dbo.JobQueue");
            ExecuteSql("TRUNCATE TABLE dbo.ResourceCurrent");
            ExecuteSql("TRUNCATE TABLE dbo.ResourceHistory");
            var surrId = DateTime.UtcNow.DateToId();
            ExecuteSql(@$"
INSERT INTO Resource 
        (ResourceTypeId,ResourceId,Version,IsHistory,ResourceSurrogateId,IsDeleted,RequestMethod,RawResource,IsRawResourceMetaSet,SearchParamHash)
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
