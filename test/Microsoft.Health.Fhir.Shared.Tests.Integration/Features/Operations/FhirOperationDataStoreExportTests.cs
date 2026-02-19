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

        [Fact]
        public async Task GivenAMatchingExportJob_WhenGettingById_ThenTheMatchingExportJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(jobRecord.Id, outcome?.JobRecord?.Id);
        }

        [Fact]
        public async Task GivenNoMatchingExportJob_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync("test", CancellationToken.None));
        }

        [Fact]
        public async Task GivenThereIsNoRunningExportJob_WhenAcquiringExportJobs_ThenAvailableExportJobsShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();

            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync();

            // The job should be marked as running now since it's acquired.
            jobRecord.Status = OperationStatus.Running;

            Assert.NotNull(jobs);
            Assert.Collection(
                jobs,
                job => ValidateExportJobOutcome(jobRecord, job.JobRecord));
        }

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

        [Theory]
        [InlineData(JobStatus.Cancelled, false)]
        [InlineData(JobStatus.Completed, true)]
        [InlineData(JobStatus.Failed, true)]
        public async Task GivenACancelledOrCancelRequestedExportJob_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown(JobStatus status, bool cancelRequested)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var jobInfo = queueClient.JobInfos.First(j => j.Id == jobId);
            jobInfo.Status = status;
            jobInfo.CancelRequested = cancelRequested;

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNumericNonExistentId_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync("999999", CancellationToken.None));
        }

        [Fact]
        public async Task GivenFailedExportJobWithoutGroupCancelRequested_WhenGettingById_ThenOutcomeShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var jobInfo = queueClient.JobInfos.First(j => j.Id == jobId);
            jobInfo.Status = JobStatus.Failed;

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
        }

        [Fact]
        public async Task GivenRunningExportJob_WhenGettingById_ThenOutcomeShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var jobInfo = queueClient.JobInfos.First(j => j.Id == jobId);
            jobInfo.Status = JobStatus.Running;

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.Equal(OperationStatus.Running, outcome.JobRecord.Status);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithFailedChildJobWithResultButNoFailureDetails_WhenGettingById_ThenStatusShouldBeFailed()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add a failed child job with result that has no FailureDetails
            var failedChildRecord = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64);

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord),
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithDuplicateFailureReasons_WhenGettingById_ThenFailureReasonShouldNotBeDuplicated()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add two failed child jobs with the same failure reason
            var failedChildRecord = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                FailureDetails = new JobFailureDetails("Same error", HttpStatusCode.InternalServerError),
            };

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord),
                CreateDate = DateTime.UtcNow,
            });

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 101,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord),
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.FailureDetails);

            // The reason should appear only once since duplicates are skipped
            int count = outcome.JobRecord.FailureDetails.FailureReason.Split("Same error").Length - 1;
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithFailedChildJobs_WhenGettingById_ThenStatusShouldBeFailed()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add a failed child job with failure details
            var failedChildRecord = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                FailureDetails = new JobFailureDetails("Child job failed", HttpStatusCode.InternalServerError),
            };

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord),
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.FailureDetails);
            Assert.Contains("Child job failed", outcome.JobRecord.FailureDetails.FailureReason);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithFailedChildJobAndNoResult_WhenGettingById_ThenStatusShouldBeFailedWithDefaultMessage()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add a failed child job with no result
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = null,
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.FailureDetails);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithMultipleFailedChildJobs_WhenGettingById_ThenFailureReasonsShouldBeConcatenated()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add first failed child job
            var failedChildRecord1 = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                FailureDetails = new JobFailureDetails("Error A", HttpStatusCode.InternalServerError),
            };
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord1),
                CreateDate = DateTime.UtcNow,
            });

            // Add second failed child job with different error
            var failedChildRecord2 = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                FailureDetails = new JobFailureDetails("Error B", HttpStatusCode.InternalServerError),
            };
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 101,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord2),
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.FailureDetails);
            Assert.Contains("Error A", outcome.JobRecord.FailureDetails.FailureReason);
            Assert.Contains("Error B", outcome.JobRecord.FailureDetails.FailureReason);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithCancelledChildJobs_WhenGettingById_ThenStatusShouldBeCancelled()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add a cancelled child job
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Cancelled,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Canceled, outcome.JobRecord.Status);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithInFlightChildJobs_WhenGettingById_ThenStatusShouldBeRunning()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add a running child job
            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Running,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Running, outcome.JobRecord.Status);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithAllChildrenCompleted_WhenGettingById_ThenOutputShouldBeMerged()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // Add a completed child job with output
            var childRecord = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64);
            childRecord.Output.Add("Patient", new List<ExportFileInfo> { new ExportFileInfo("Patient", new Uri("http://test/Patient-1.ndjson"), 0) });

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Completed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(childRecord),
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Completed, outcome.JobRecord.Status);
            var hasPatientOutput = outcome.JobRecord.Output.TryGetValue("Patient", out var patientOutput);
            Assert.True(hasPatientOutput);
            Assert.Single(patientOutput);
        }

        [Fact]
        public async Task GivenCompletedExportJobWithMultipleChildrenSameOutputKey_WhenGettingById_ThenOutputShouldBeAggregated()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;

            // First completed child with Patient output
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

            // Second completed child with same Patient output key
            var childRecord2 = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64);
            childRecord2.Output.Add("Patient", new List<ExportFileInfo> { new ExportFileInfo("Patient", new Uri("http://test/Patient-2.ndjson"), 1) });

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

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Completed, outcome.JobRecord.Status);
            Assert.True(outcome.JobRecord.Output.TryGetValue("Patient", out var patientFiles));
            Assert.Equal(2, patientFiles.Count);
        }

        [Fact]
        public async Task GivenCompletedParentJobWithUserCancelledFlag_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;
            parentJob.CancelRequested = true;

            // No child job failures - this is user cancellation
            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None));
        }

        [Fact]
        public async Task GivenCompletedParentJobWithCancelFlagButChildJobFailed_WhenGettingById_ThenStatusShouldBeFailed()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();
            long jobId = long.Parse(jobRecord.Id);

            var queueClient = GetTestQueueClient();
            var parentJob = queueClient.JobInfos.First(j => j.Id == jobId);
            parentJob.Status = JobStatus.Completed;
            parentJob.CancelRequested = true;

            // Add a failed child job - this is system cancellation due to failure
            var failedChildRecord = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                FailureDetails = new JobFailureDetails("Child job failed", HttpStatusCode.InternalServerError),
            };

            queueClient.JobInfos.Add(new JobInfo
            {
                Id = jobId + 100,
                GroupId = parentJob.GroupId,
                Status = JobStatus.Failed,
                QueueType = (byte)QueueType.Export,
                Definition = parentJob.Definition,
                Result = JsonConvert.SerializeObject(failedChildRecord),
                CreateDate = DateTime.UtcNow,
            });

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(OperationStatus.Failed, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.FailureDetails);
        }

        [Fact]
        public async Task GivenNonExistentExportJob_WhenUpdating_ThenJobNotFoundExceptionShouldBeThrown()
        {
            var jobRecord = new ExportJobRecord(
                _exportRequest.RequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                Id = "999999",
            };

            await Assert.ThrowsAsync<JobNotFoundException>(
                () => _operationDataStore.UpdateExportJobAsync(jobRecord, null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnExportJob_WhenUpdating_ThenJobShouldBeCancelledAndOutcomeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.UpdateExportJobAsync(
                jobRecord, WeakETag.FromVersionId("0"), CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.Equal(jobRecord.Id, outcome.JobRecord.Id);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenUpdatingWithNullETag_ThenDefaultETagShouldBeUsed()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.UpdateExportJobAsync(jobRecord, null, CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.NotNull(outcome.ETag);
        }

        private async Task<ExportJobOutcome> CreateRunningExportJob()
        {
            // Create a queued job.
            await InsertNewExportJobRecordAsync();

            // Acquire the job. This will timestamp it and set it to running.
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
            if (jobRecord.Status != OperationStatus.Queued && jobRecord.Status != result.JobRecord.Status) // SQL enqueues only queued and completed
            {
                jobRecord.Id = result.JobRecord.Id;
                if (jobRecord.Status == OperationStatus.Canceled)
                {
                    await _operationDataStore.UpdateExportJobAsync(jobRecord, null, CancellationToken.None);
                }
                else if (jobRecord.Status == OperationStatus.Failed)
                {
                    var single = (await _operationDataStore.AcquireExportJobsAsync(1, TimeSpan.FromSeconds(600), CancellationToken.None)).First();
                    single.JobRecord.Status = jobRecord.Status;
                    await _operationDataStore.UpdateExportJobAsync(single.JobRecord, single.ETag, CancellationToken.None);
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
    }
}
