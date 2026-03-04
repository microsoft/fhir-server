// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [Collection(FhirOperationTestConstants.FhirOperationTests)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class FhirOperationDataStoreExportTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly IFhirOperationDataStore _operationDataStore;
        private readonly IFhirStorageTestHelper _testHelper;

        private readonly CreateExportRequest _exportRequest = new CreateExportRequest(new Uri("http://localhost/ExportJob"), ExportJobType.All);

        public FhirOperationDataStoreExportTests(FhirStorageTestsFixture fixture)
        {
            _operationDataStore = fixture.OperationDataStore;
            _testHelper = fixture.TestHelper;
        }

        public async Task InitializeAsync()
        {
            await _testHelper.DeleteAllExportJobRecordsAsync();
            GetTestQueueClient().ClearJobs();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Verifies that creating an export job via <see cref="IFhirOperationDataStore.CreateExportJobAsync"/>
        /// returns a valid outcome with a non-empty Id and ETag.
        /// </summary>
        [Fact]
        public async Task GivenANewExportRequest_WhenCreatingAnExportJob_ThenAnExportJobGetsCreated()
        {
            var jobRecord = new ExportJobRecord(_exportRequest.RequestUri, _exportRequest.RequestType, ExportFormatTags.ResourceName, _exportRequest.ResourceType, null, "hash", rollingFileSizeInMB: 64);

            ExportJobOutcome outcome = await _operationDataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.NotNull(outcome.JobRecord);
            Assert.NotEmpty(outcome.JobRecord.Id);
            Assert.NotNull(outcome.ETag);
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> returns the
        /// matching job when called with a valid job Id.
        /// </summary>
        [Fact]
        public async Task GivenAMatchingExportJob_WhenGettingById_ThenTheMatchingExportJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(jobRecord.Id, outcome?.JobRecord?.Id);
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> throws
        /// <see cref="JobNotFoundException"/> for invalid job Ids:
        ///   - Non-numeric Id ("test") → fails long.TryParse, throws immediately.
        ///   - Numeric Id with no matching job ("999999") → GetJobByIdAsync returns null, throws.
        /// </summary>
        [Theory]
        [InlineData("test")]
        [InlineData("999999")]
        public async Task GivenNoMatchingExportJob_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown(string jobId)
        {
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(jobId, CancellationToken.None));
        }

        /// <summary>
        /// Verifies that when the orchestrator is Completed and has no child jobs (only itself
        /// in the group), <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> returns
        /// Completed status with empty output.
        /// </summary>
        [Fact]
        public async Task GivenCompletedExportJobWithNoChildren_WhenGettingById_ThenStatusShouldBeCompleted()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Completed, outcome.JobRecord.Status);
            Assert.Empty(outcome.JobRecord.Output);
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.AcquireExportJobsAsync"/> returns
        /// available queued jobs and transitions them to Running status.
        /// </summary>
        [Fact]
        public async Task GivenThereIsNoRunningExportJob_WhenAcquiringExportJobs_ThenAvailableExportJobsShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();

            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync();

            jobRecord.Status = OperationStatus.Running;

            Assert.NotNull(jobs);
            Assert.Collection(
                jobs,
                job => ValidateExportJobOutcome(jobRecord, job.JobRecord));
        }

        /// <summary>
        /// Verifies that a running export job whose heartbeat has expired is re-acquired
        /// when <see cref="IFhirOperationDataStore.AcquireExportJobsAsync"/> is called with
        /// a threshold shorter than the elapsed time since the last heartbeat.
        /// </summary>
        [Fact]
        public async Task GivenThereIsARunningExportJobThatExpired_WhenAcquiringExportJobs_ThenTheExpiredExportJobShouldBeReturned()
        {
            ExportJobOutcome jobOutcome = await CreateRunningExportJob();

            await Task.Delay(2000);

            IReadOnlyCollection<ExportJobOutcome> expiredJobs = await AcquireExportJobsAsync(jobHeartbeatTimeoutThreshold: TimeSpan.FromSeconds(1));

            Assert.NotNull(expiredJobs);
            Assert.Collection(
                expiredJobs,
                expiredJobOutcome => ValidateExportJobOutcome(jobOutcome.JobRecord, expiredJobOutcome.JobRecord));
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> returns the job
        /// with the corresponding mapped <see cref="OperationStatus"/> for each non-Completed queue status.
        /// The method does NOT throw <see cref="JobNotFoundException"/> for these statuses — it simply
        /// passes the status through to <c>CreateExportJobOutcome</c> which casts <c>(byte)status</c>
        /// to <see cref="OperationStatus"/>.
        ///
        /// The only path that throws <see cref="JobNotFoundException"/> is when a CancelledByUser
        /// processing job exists in the group (checked before status evaluation).
        /// </summary>
        [Theory]
        [InlineData(JobStatus.Created, OperationStatus.Queued)]
        [InlineData(JobStatus.Running, OperationStatus.Running)]
        [InlineData(JobStatus.Failed, OperationStatus.Failed)]
        [InlineData(JobStatus.Cancelled, OperationStatus.Canceled)]
        public async Task GivenExportJobInNonCompletedStatus_WhenGettingById_ThenMatchingOperationStatusShouldBeReturned(JobStatus jobStatus, OperationStatus expectedStatus)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var jobInfo = queueClient.JobInfos.First(j => j.Id == jobId);
            jobInfo.Status = jobStatus;

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.Equal(expectedStatus, outcome.JobRecord.Status);
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> throws
        /// <see cref="JobNotFoundException"/> when a CancelledByUser processing job exists in the
        /// job group. This is the ONLY mechanism that causes GetExportJobByIdAsync to return 404
        /// for a job that exists in the queue. It is triggered by user-initiated cancellations
        /// (isCustomerRequested = true in UpdateExportJobAsync) which enqueue a CancelledByUser
        /// marker job per the FHIR Bulk Data IG.
        ///
        /// This check happens regardless of the orchestrator's own status — even if the
        /// orchestrator is Completed, Running, or any other status.
        /// </summary>
        [Theory]
        [InlineData(JobStatus.Created)]
        [InlineData(JobStatus.Running)]
        [InlineData(JobStatus.Completed)]
        [InlineData(JobStatus.Failed)]
        [InlineData(JobStatus.Cancelled)]
        public async Task GivenExportJobGroupWithCancelledByUserChild_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown(JobStatus parentStatus)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = parentStatus;

            // Add a CancelledByUser marker job in the group
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.CancelledByUser,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                CreateDate = DateTime.UtcNow,
            });

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None));
        }

        /// <summary>
        /// Verifies that when the orchestrator is Completed but child job(s) failed,
        /// <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> returns Failed status.
        /// Covers five cases based on the child's result and multiplicity:
        ///   - "NoResult": Single child with no result (null) → Failed with default ProcessingJobHadNoResults message.
        ///   - "ResultWithoutFailureDetails": Single child with a serialized result but no FailureDetails →
        ///     Failed, but parent FailureDetails remains null (nothing to propagate).
        ///   - "ResultWithFailureDetails": Single child with FailureDetails →
        ///     Failed with the child's failure reason propagated.
        ///   - "MultipleChildrenSameError": Two children with identical failure reasons →
        ///     Failed, reason appears only once (deduplicated by case-insensitive comparison).
        ///   - "MultipleChildrenDifferentErrors": Two children with different failure reasons →
        ///     Failed, both reasons concatenated with "\r\n".
        /// </summary>
        [Theory]
        [InlineData("NoResult")]
        [InlineData("ResultWithoutFailureDetails")]
        [InlineData("ResultWithFailureDetails")]
        [InlineData("MultipleChildrenSameError")]
        [InlineData("MultipleChildrenDifferentErrors")]
        public async Task GivenCompletedExportJobWithFailedChildJob_WhenGettingById_ThenStatusShouldBeFailed(string childResultType)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            switch (childResultType)
            {
                case "NoResult":
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, null);
                    break;

                case "ResultWithoutFailureDetails":
                    var recordNoDetails = new ExportJobRecord(
                        _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64);
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, JsonConvert.SerializeObject(recordNoDetails));
                    break;

                case "ResultWithFailureDetails":
                    var recordWithDetails = new ExportJobRecord(
                        _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
                    {
                        FailureDetails = new JobFailureDetails("Child job failed", HttpStatusCode.InternalServerError),
                    };
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, JsonConvert.SerializeObject(recordWithDetails));
                    break;

                case "MultipleChildrenSameError":
                    var sameErrorRecord = new ExportJobRecord(
                        _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
                    {
                        FailureDetails = new JobFailureDetails("Same error", HttpStatusCode.InternalServerError),
                    };
                    string sameErrorResult = JsonConvert.SerializeObject(sameErrorRecord);
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, sameErrorResult, idOffset: 100);
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, sameErrorResult, idOffset: 101);
                    break;

                case "MultipleChildrenDifferentErrors":
                    var errorARecord = new ExportJobRecord(
                        _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
                    {
                        FailureDetails = new JobFailureDetails("Error A", HttpStatusCode.InternalServerError),
                    };
                    var errorBRecord = new ExportJobRecord(
                        _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
                    {
                        FailureDetails = new JobFailureDetails("Error B", HttpStatusCode.InternalServerError),
                    };
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, JsonConvert.SerializeObject(errorARecord), idOffset: 100);
                    AddFailedChildJob(queueClient, jobId, parentJob.GroupId, parentJob.Definition, JsonConvert.SerializeObject(errorBRecord), idOffset: 101);
                    break;
            }

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);

            switch (childResultType)
            {
                case "NoResult":
                    Assert.NotNull(outcome.JobRecord.FailureDetails);
                    Assert.Contains(Core.Resources.ProcessingJobHadNoResults, outcome.JobRecord.FailureDetails.FailureReason);
                    break;
                case "ResultWithoutFailureDetails":
                    Assert.Null(outcome.JobRecord.FailureDetails);
                    break;
                case "ResultWithFailureDetails":
                    Assert.NotNull(outcome.JobRecord.FailureDetails);
                    Assert.Contains("Child job failed", outcome.JobRecord.FailureDetails.FailureReason);
                    break;
                case "MultipleChildrenSameError":
                    Assert.NotNull(outcome.JobRecord.FailureDetails);
                    int count = outcome.JobRecord.FailureDetails.FailureReason.Split("Same error").Length - 1;
                    Assert.Equal(1, count);
                    break;
                case "MultipleChildrenDifferentErrors":
                    Assert.NotNull(outcome.JobRecord.FailureDetails);
                    Assert.Contains("Error A", outcome.JobRecord.FailureDetails.FailureReason);
                    Assert.Contains("Error B", outcome.JobRecord.FailureDetails.FailureReason);
                    break;
            }
        }

        /// <summary>
        /// Verifies that when the orchestrator is Completed, the aggregated status returned by
        /// <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> depends on child job states:
        ///   - Created child (in-flight) → Running (jobs still pending pickup).
        ///   - Running child with CancelRequested → Canceled (treated as cancelled, no failures).
        ///   - Cancelled child (no failures) → Canceled (system-level cancellation without CancelledByUser marker).
        /// </summary>
        [Theory]
        [InlineData(JobStatus.Created, false, OperationStatus.Running)]
        [InlineData(JobStatus.Running, true, OperationStatus.Canceled)]
        [InlineData(JobStatus.Cancelled, false, OperationStatus.Canceled)]
        public async Task GivenCompletedExportJobWithChildInVariousStates_WhenGettingById_ThenAggregatedStatusShouldBeCorrect(JobStatus childStatus, bool childCancelRequested, OperationStatus expectedStatus)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = childStatus,
                CancelRequested = childCancelRequested,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(expectedStatus, outcome.JobRecord.Status);
        }

        /// <summary>
        /// Verifies that when the orchestrator is Completed and all children completed successfully,
        /// <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> merges output from child jobs
        /// into the parent record. Tests three cases:
        ///   - Distinct output keys → both keys present with one file each.
        ///   - Same output key → files aggregated under one key.
        ///   - Child Result is the literal string "null" → skipped during merging, output is empty.
        /// </summary>
        [Theory]
        [InlineData("DistinctKeys")]
        [InlineData("SameKey")]
        [InlineData("NullStringResult")]
        public async Task GivenCompletedExportJobWithAllChildrenCompleted_WhenGettingById_ThenOutputShouldBeMerged(string scenario)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            if (scenario == "NullStringResult")
            {
                queueClient.JobInfos.Add(new JobInfo
                {
                    Id = jobId + 100,
                    GroupId = parentJob.GroupId,
                    Status = JobStatus.Completed,
                    QueueType = (byte)QueueType.Export,
                    Definition = parentJob.Definition,
                    Result = "null",
                    CreateDate = DateTime.UtcNow,
                });
            }
            else
            {
                var childRecord1 = new ExportJobRecord(
                    _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64);
                childRecord1.Output.Add("Patient", new List<ExportFileInfo> { new ExportFileInfo("Patient", new Uri("http://test/Patient-1.ndjson"), 0) });

                queueClient.JobInfos.Add(new JobInfo
                {
                    Id = jobId + 100,
                    GroupId = parentJob.GroupId,
                    Status = JobStatus.Completed,
                    QueueType = (byte)QueueType.Export,
                    Definition = parentJob.Definition,
                    Result = JsonConvert.SerializeObject(childRecord1),
                    CreateDate = DateTime.UtcNow,
                });

                string secondKey = scenario == "SameKey" ? "Patient" : "Observation";
                var childRecord2 = new ExportJobRecord(
                    _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64);
                childRecord2.Output.Add(secondKey, new List<ExportFileInfo> { new ExportFileInfo(secondKey, new Uri($"http://test/{secondKey}-2.ndjson"), 1) });

                queueClient.JobInfos.Add(new JobInfo
                {
                    Id = jobId + 101,
                    GroupId = parentJob.GroupId,
                    Status = JobStatus.Completed,
                    QueueType = (byte)QueueType.Export,
                    Definition = parentJob.Definition,
                    Result = JsonConvert.SerializeObject(childRecord2),
                    CreateDate = DateTime.UtcNow,
                });
            }

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Completed, outcome.JobRecord.Status);

            switch (scenario)
            {
                case "SameKey":
                    Assert.True(outcome.JobRecord.Output.TryGetValue("Patient", out var sameKeyFiles));
                    Assert.Equal(2, sameKeyFiles.Count);
                    break;
                case "DistinctKeys":
                    Assert.True(outcome.JobRecord.Output.TryGetValue("Patient", out var patientFiles));
                    Assert.Single(patientFiles);
                    Assert.True(outcome.JobRecord.Output.TryGetValue("Observation", out var observationFiles));
                    Assert.Single(observationFiles);
                    break;
                case "NullStringResult":
                    Assert.Empty(outcome.JobRecord.Output);
                    break;
            }
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/> with
        /// isCustomerRequested = false cancels the job group (Created→Cancelled, Running→CancelRequested)
        /// and returns a valid outcome with the Canceled status and an ETag (defaulted when null).
        /// No CancelledByUser marker job is enqueued.
        /// </summary>
        [Fact]
        public async Task GivenAnExportJob_WhenUpdatingWithCanceledStatus_ThenQueueJobsShouldBeCancelledAndOutcomeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();

            var queueClient = GetTestQueueClient();
            long jobId = long.Parse(jobRecord.Id);
            var jobInfo = queueClient.JobInfos.First(j => j.Id == jobId);

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = jobInfo.GroupId,
                Status = JobStatus.Running,
                QueueType = (byte)QueueType.Export,
                Definition = jobInfo.Definition,
                CreateDate = DateTime.UtcNow,
            });

            jobRecord.Status = OperationStatus.Canceled;

            ExportJobOutcome outcome = await _operationDataStore.UpdateExportJobAsync(jobRecord, null, false, CancellationToken.None);

            // Verify outcome
            Assert.NotNull(outcome);
            Assert.Equal(jobRecord.Id, outcome.JobRecord.Id);
            Assert.Equal(OperationStatus.Canceled, outcome.JobRecord.Status);
            Assert.NotNull(outcome.ETag);

            // Verify queue-level cancellation
            var updatedParent = queueClient.JobInfos.First(j => j.Id == jobId);
            var updatedChild = queueClient.JobInfos.First(j => j.Id == jobId + 100);

            Assert.Equal(JobStatus.Cancelled, updatedParent.Status);
            Assert.True(updatedChild.CancelRequested);

            // Verify no CancelledByUser marker was enqueued
            bool hasCancelledByUserJob = queueClient.JobInfos.Any(j => j.GroupId == jobInfo.GroupId && j.Status == JobStatus.CancelledByUser);
            Assert.False(hasCancelledByUserJob);
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/> throws
        /// <see cref="NotSupportedException"/> for any status other than Canceled.
        /// Only Canceled status is supported; all other statuses are deprecated.
        /// </summary>
        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        public async Task GivenAnExportJob_WhenUpdatingWithNonCanceledStatus_ThenNotSupportedExceptionShouldBeThrown(OperationStatus status)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            jobRecord.Status = status;

            await Assert.ThrowsAsync<NotSupportedException>(
                () => _operationDataStore.UpdateExportJobAsync(jobRecord, WeakETag.FromVersionId("0"), false, CancellationToken.None));
        }

        /// <summary>
        /// Verifies that <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/> with
        /// isCustomerRequested = true cancels the job group AND enqueues a CancelledByUser
        /// processing job via EnqueueWithStatusAsync. Subsequent GetExportJobByIdAsync calls
        /// throw <see cref="JobNotFoundException"/> due to the CancelledByUser marker.
        /// </summary>
        [Fact]
        public async Task GivenAnExportJob_WhenUpdatingWithCustomerRequestedCancel_ThenCancelledByUserJobShouldBeEnqueued()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var jobInfo = queueClient.JobInfos.First(j => j.Id == jobId);

            jobRecord.Status = OperationStatus.Canceled;

            await _operationDataStore.UpdateExportJobAsync(jobRecord, null, true, CancellationToken.None);

            // Verify a CancelledByUser job was enqueued in the group
            bool hasCancelledByUserJob = queueClient.JobInfos.Any(j => j.GroupId == jobInfo.GroupId && j.Status == JobStatus.CancelledByUser);
            Assert.True(hasCancelledByUserJob);

            // Verify GetExportJobByIdAsync now throws 404
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None));
        }

        private async Task<ExportJobOutcome> CreateRunningExportJob()
        {
            await InsertNewExportJobRecordAsync();

            IReadOnlyCollection<ExportJobOutcome> jobOutcomes = await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);

            Assert.NotNull(jobOutcomes);
            Assert.Single(jobOutcomes);

            ExportJobOutcome jobOutcome = jobOutcomes.FirstOrDefault();

            Assert.NotNull(jobOutcome);
            Assert.NotNull(jobOutcome.JobRecord);
            Assert.Equal(OperationStatus.Running, jobOutcome.JobRecord.Status);

            return jobOutcome;
        }

        private async Task<ExportJobRecord> InsertNewExportJobRecordAsync(Action<ExportJobRecord> jobRecordCustomizer = null)
        {
            var jobRecord = new ExportJobRecord(_exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "Unknown", rollingFileSizeInMB: 64, storageAccountContainerName: Guid.NewGuid().ToString());

            jobRecordCustomizer?.Invoke(jobRecord);

            var result = await _operationDataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);
            if (jobRecord.Status != OperationStatus.Queued && jobRecord.Status != result.JobRecord.Status)
            {
                jobRecord.Id = result.JobRecord.Id;
                if (jobRecord.Status == OperationStatus.Canceled)
                {
                    await _operationDataStore.UpdateExportJobAsync(jobRecord, null, false, CancellationToken.None);
                }
                else if (jobRecord.Status == OperationStatus.Failed)
                {
                    var single = (await _operationDataStore.AcquireExportJobsAsync(1, TimeSpan.FromSeconds(600), CancellationToken.None)).First();
                    single.JobRecord.Status = jobRecord.Status;
                    await _operationDataStore.UpdateExportJobAsync(single.JobRecord, single.ETag, false, CancellationToken.None);
                }
            }

            return result.JobRecord;
        }

        private async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(
            ushort maximumNumberOfConcurrentJobAllowed = 1,
            TimeSpan? jobHeartbeatTimeoutThreshold = null)
        {
            if (jobHeartbeatTimeoutThreshold == null)
            {
                jobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(1);
            }

            return await _operationDataStore.AcquireExportJobsAsync(
                maximumNumberOfConcurrentJobAllowed,
                jobHeartbeatTimeoutThreshold.Value,
                CancellationToken.None);
        }

        private void ValidateExportJobOutcome(ExportJobRecord expected, ExportJobRecord actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.CanceledTime, actual.CanceledTime);
            Assert.Equal(expected.EndTime, actual.EndTime);
            Assert.Equal(expected.Hash, actual.Hash);
            Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
            Assert.Equal(expected.StartTime, actual.StartTime);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.QueuedTime, actual.QueuedTime);
        }

        private TestQueueClient GetTestQueueClient()
        {
            var operationDataStoreBase = _operationDataStore as FhirOperationDataStoreBase;
            if (operationDataStoreBase == null)
            {
                throw new InvalidOperationException("Operation data store is not of type FhirOperationDataStoreBase");
            }

            var field = typeof(FhirOperationDataStoreBase).GetField("_queueClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var queueClient = field?.GetValue(operationDataStoreBase) as TestQueueClient;

            if (queueClient == null)
            {
                throw new InvalidOperationException("Could not retrieve TestQueueClient from operation data store");
            }

            return queueClient;
        }

        private static void AddFailedChildJob(TestQueueClient queueClient, long parentJobId, long groupId, string definition, string result, int idOffset = 100)
        {
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = parentJobId + idOffset,
                GroupId = groupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = definition,
                Result = result,
                CreateDate = DateTime.UtcNow,
            });
        }
    }
}
