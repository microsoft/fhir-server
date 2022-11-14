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
            PrepareData(); // 1000 patients and 1000 obesrvations

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(600));
            var worker = Task.Factory.StartNew(() => Worker(cts.Token));

            var coord = new ExportOrchestratorJob(_queueClient, _searchService, null);
            coord.PollingFrequencyInSeconds = 1;
            coord.SurrogateIdRangeSize = 100;
            coord.NumberOfSurrogateIdRanges = 5;

            await Check("Patient", 11, coord, cts.Token); // coord + 1000/SurrogateIdRangeSize

            await Check("Patient,Observation", 21, coord, cts.Token); // coord + 1000/SurrogateIdRangeSize + 1000/SurrogateIdRangeSize

            await Check(null, 21, coord, cts.Token); // coord + 1000/SurrogateIdRangeSize + 1000/SurrogateIdRangeSize

            cts.Cancel();
            try
            {
                await worker;
            }
            catch
            {
            }
        }

        private async Task Check(string format, int expectedNumberOfJobs, ExportOrchestratorJob coord, CancellationToken cancel)
        {
            var coordRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, format, null, Guid.NewGuid().ToString(), 1, parallel: 2);
            var result = await _operationDataStore.CreateExportJobAsync(coordRecord, cancel);
            Assert.NotNull(result?.JobRecord?.Id);
            var groupId = (await _queueClient.GetJobByIdAsync((byte)QueueType.Export, long.Parse(result.JobRecord.Id), false, cancel)).GroupId;

            coordRecord = (await _operationDataStore.AcquireExportJobsAsync(1, TimeSpan.FromSeconds(60), cancel)).First().JobRecord;
            var coordTask = coord.ExecuteAsync(ToJobInfo(coordRecord), new Progress<string>(), cancel);
            coordTask.Wait(cancel);
            var jobs = (await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Export, groupId, false, cancel)).ToList();
            Assert.Equal(expectedNumberOfJobs, jobs.Count);
        }

        private void Worker(CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                var job = _queueClient.DequeueAsync((byte)QueueType.Export, "Whatever", 60, cancel).Result;
                if (job != null)
                {
                    job.Result = job.Definition;
                    _queueClient.CompleteJobAsync(job, false, cancel).Wait();
                }
                else
                {
                    Task.Delay(1000, cancel).Wait(cancel);
                }
            }
        }

        private JobManagement.JobInfo ToJobInfo(ExportJobRecord record)
        {
            var jobInfo = new JobManagement.JobInfo();
            jobInfo.QueueType = (byte)QueueType.Export;
            jobInfo.Id = long.Parse(record.Id);
            jobInfo.GroupId = record.GroupId == null ? jobInfo.Id : long.Parse(record.GroupId); // TODO: Remove hack
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
         CROSS JOIN (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name IN ('Patient','Observation')) B
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
