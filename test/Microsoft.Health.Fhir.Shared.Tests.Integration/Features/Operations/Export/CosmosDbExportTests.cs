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

        /// <summary>
        /// Verifies user-initiated cancellation via <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/>
        /// with isCustomerRequested = true. This causes:
        ///   1. CancelJobByGroupIdAsync to cancel all jobs in the group.
        ///   2. A CancelledByUser processing job to be enqueued via EnqueueWithStatusAsync.
        ///
        /// Subsequent calls to <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> detect the
        /// CancelledByUser job in the group and throw <see cref="JobNotFoundException"/> for both
        /// the orchestrator and child processing job IDs, confirming the FHIR Bulk Data IG
        /// delete-request behavior.
        /// </summary>
        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenUserCancelled_ThenBothCoordAndChildJobIdsReturn404()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var coordId = long.Parse(coorId);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            var groupJobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, CancellationToken.None)).ToList();
            var childJobId = groupJobs.First(j => j.Id != coordId).Id.ToString();

            var record = (await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None)).JobRecord;
            Assert.Equal(OperationStatus.Running, record.Status);

            record.Status = OperationStatus.Canceled;
            var result = await _operationDataStore.UpdateExportJobAsync(record, null, true, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);

            // CancelledByUser processing job now exists in the group — both IDs should throw 404
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None));
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(childJobId, CancellationToken.None));
        }

        /// <summary>
        /// Verifies system-level cancellation via <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/>
        /// with isCustomerRequested = false. This cancels the group but does NOT enqueue a CancelledByUser job.
        /// Without the CancelledByUser marker, <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/>
        /// returns the job with Canceled status (via the cancelled processing jobs in the group)
        /// instead of throwing <see cref="JobNotFoundException"/>.
        /// </summary>
        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenSystemCancelled_ThenJobReturnsCanceledStatus()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var record = (await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None)).JobRecord;

            Assert.Equal(OperationStatus.Running, record.Status);

            record.Status = OperationStatus.Canceled;
            var result = await _operationDataStore.UpdateExportJobAsync(record, null, false, CancellationToken.None);

            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);

            // No CancelledByUser job, so GetExportJobByIdAsync returns the job with Canceled status
            var outcome = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, outcome.JobRecord.Status);
        }

        /// <summary>
        /// Verifies that when a child processing job fails, <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/>
        /// aggregates the child's failure details into the orchestrator record and returns Failed status.
        /// </summary>
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

            // GetExportJobByIdAsync aggregates child failure details and returns Failed status
            var result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Failed, result.JobRecord.Status);
            Assert.NotNull(result.JobRecord.FailureDetails);
        }

        /// <summary>
        /// Verifies that when all child processing jobs complete successfully,
        /// <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> returns Completed status.
        ///
        /// Note: GetExportJobByIdAsync treats recently completed jobs (EndDate within 15 seconds)
        /// as still in-flight to handle race conditions with dequeue. The test polls until the
        /// 15-second window passes and the status settles to Completed.
        /// </summary>
        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenChildJobCompletes_JobStatusIsCorrect()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var coordId = long.Parse(coorId);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            // Dequeue and complete all child jobs
            var groupJobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, CancellationToken.None)).ToList();
            foreach (var childJob in groupJobs.Where(j => j.Id != coordId))
            {
                var dequeuedChild = await _queueClient.DequeueAsync(_queueType, "Worker", 60, CancellationToken.None, childJob.Id);
                dequeuedChild.Status = JobStatus.Completed;
                dequeuedChild.Result = Newtonsoft.Json.JsonConvert.SerializeObject(new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1));
                await _queueClient.CompleteJobAsync(dequeuedChild, false, CancellationToken.None);
            }

            // Poll until the 15-second in-flight window passes and status settles
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

        /// <summary>
        /// Verifies that the orchestrator creates the expected number of jobs based on resource types:
        ///   - "Patient,Observation": 3 jobs (1 orchestrator + 2 resource types).
        ///   - null (all types): 4 jobs (1 orchestrator + 3 resource types seeded by the fixture: Patient, Observation, Claim).
        /// </summary>
        [Theory]
        [InlineData("Patient,Observation", 3)]
        [InlineData(null, 4)]
        public async Task GivenAnExport_WhenQueued_JobCountMatchesExpectedResourceTypes(string resourceType, int expectedTotalJobs)
        {
            await RunExport(resourceType, _coordJob, expectedTotalJobs);
        }

        /// <summary>
        /// Verifies that when a child job fails with no result (null/empty), GetExportJobByIdAsync
        /// returns Failed status with a default failure message indicating the processing job
        /// produced no results.
        /// </summary>
        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenChildJobFailsWithNoResult_ReturnsFailedWithDefaultMessage()
        {
            string resourceType = "Patient";
            int totalJobs = 2;
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var coordId = long.Parse(coorId);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            var groupJobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, CancellationToken.None)).ToList();
            var childJob = groupJobs.First(j => j.Id != coordId);
            var dequeuedChild = await _queueClient.DequeueAsync(_queueType, "Worker", 60, CancellationToken.None, childJob.Id);

            // Complete as failed with no result
            dequeuedChild.Status = JobStatus.Failed;
            dequeuedChild.Result = null;
            await _queueClient.CompleteJobAsync(dequeuedChild, true, CancellationToken.None);

            var result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Failed, result.JobRecord.Status);
            Assert.NotNull(result.JobRecord.FailureDetails);
            Assert.Contains(Core.Resources.ProcessingJobHadNoResults, result.JobRecord.FailureDetails.FailureReason);
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
