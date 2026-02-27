// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosDbExportTests : IClassFixture<CosmosDbFhirExportTestFixture>
    {
        private readonly CosmosDbFhirStorageTestsFixture _fixture;
        private readonly Func<IScoped<ISearchService>> _searchServiceScopeFactory;
        private readonly CosmosFhirOperationDataStore _operationDataStore;
        private readonly IQueueClient _queueClient;
        private readonly byte _queueType = (byte)QueueType.Export;
        private readonly CosmosExportOrchestratorJob _coordJob;
        private readonly ILogger<CosmosExportOrchestratorJob> _logger;

        public CosmosDbExportTests(CosmosDbFhirExportTestFixture fixture)
        {
            _fixture = fixture;
            _searchServiceScopeFactory = _fixture.GetService<Func<IScoped<ISearchService>>>();
            _operationDataStore = _fixture.GetService<CosmosFhirOperationDataStore>();
            _queueClient = _fixture.GetService<IQueueClient>();
            _logger = Substitute.For<ILogger<CosmosExportOrchestratorJob>>();

            _coordJob = new(_queueClient, _searchServiceScopeFactory, _logger);
        }

        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenUserCancelled_ThrowsJobNotFoundException()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var record = (await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None)).JobRecord;

            Assert.Equal(OperationStatus.Running, record.Status);

            record.Status = OperationStatus.Canceled;
            var result = await _operationDataStore.UpdateExportJobAsync(record, null, CancellationToken.None);

            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);

            // User-cancelled job with no failures should throw JobNotFoundException
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenChildJobFails_ReturnsFailedStatus()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var coordId = long.Parse(coorId);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            // Dequeue and fail the child job
            var groupJobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, CancellationToken.None)).ToList();
            var childJob = groupJobs.First(j => j.Id != coordId);
            var dequeuedChild = await _queueClient.DequeueAsync(_queueType, "Worker", 60, CancellationToken.None, childJob.Id);

            var failedRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1)
            {
                FailureDetails = new JobFailureDetails("Processing job failed", HttpStatusCode.InternalServerError),
            };
            dequeuedChild.Status = JobStatus.Failed;
            dequeuedChild.Result = Newtonsoft.Json.JsonConvert.SerializeObject(failedRecord);
            await _queueClient.CompleteJobAsync(dequeuedChild, true, CancellationToken.None);

            // System-failed job should return Failed status
            var result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Failed, result.JobRecord.Status);
            Assert.NotNull(result.JobRecord.FailureDetails);
        }

        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenChildJobCompletes_JobStatusIsCorrect()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var coordId = long.Parse(coorId);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            // Dequeue and complete all child jobs with the given status
            var groupJobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, CancellationToken.None)).ToList();
            foreach (var childJob in groupJobs.Where(j => j.Id != coordId))
            {
                var dequeuedChild = await _queueClient.DequeueAsync(_queueType, "Worker", 60, CancellationToken.None, childJob.Id);
                dequeuedChild.Status = JobStatus.Completed;
                dequeuedChild.Result = Newtonsoft.Json.JsonConvert.SerializeObject(new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1));
                await _queueClient.CompleteJobAsync(dequeuedChild, false, CancellationToken.None);
            }

            // GetExportJobByIdAsync treats recently completed jobs (EndDate within 15s) as in-flight,
            // so poll until the window passes and the status settles to the expected value.
            ExportJobOutcome result = null;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
                if (result != null && result.JobRecord.Status == OperationStatus.Completed)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            Assert.NotNull(result);
            Assert.Equal(OperationStatus.Completed, result.JobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportOfPatientObservation_WhenQueued_JobStatusAndCountIsCorrectDuringJob()
        {
            await RunExport("Patient,Observation", _coordJob, 3); // 3=coord+2 resource type
        }

        [Fact]
        public async Task GivenAnExportOfAllTypes_WhenQueued_JobStatusAndCountIsCorrectDuringJob()
        {
            await RunExport(null, _coordJob, 4); // 4=coord+3 resource type
        }

        private async Task<string> RunExport(string resourceType, CosmosExportOrchestratorJob coordJob, int totalJobs)
        {
            var coordRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1, maximumNumberOfResourcesPerQuery: 100);
            var result = await _operationDataStore.CreateExportJobAsync(coordRecord, CancellationToken.None);
            Assert.Equal(OperationStatus.Queued, result.JobRecord.Status);
            var coordId = long.Parse(result.JobRecord.Id);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            await RunCoordinator(coordJob, coordId, groupId, totalJobs);
            return coordId.ToString();
        }

        private async Task RunCoordinator(CosmosExportOrchestratorJob coordJob, long coordId, long groupId, int totalJobs)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(300));

            var jobInfo = await _queueClient.DequeueAsync(_queueType, "Coord", 60, cts.Token, coordId);

            await coordJob.ExecuteAsync(jobInfo, cts.Token);
            await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);

            var jobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
            Assert.Equal(totalJobs, jobs.Count);
        }
    }
}
