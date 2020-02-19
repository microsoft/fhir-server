// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations
{
    [Collection(FhirOperationTestConstants.FhirOperationTests)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class FhirOperationDataStoreTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly IFhirOperationDataStore _operationDataStore;
        private readonly IFhirStorageTestHelper _testHelper;

        private readonly CreateExportRequest _exportRequest = new CreateExportRequest(new Uri("http://localhost/ExportJob"));

        public FhirOperationDataStoreTests(FhirStorageTestsFixture fixture)
        {
            _operationDataStore = fixture.OperationDataStore;
            _testHelper = fixture.TestHelper;
        }

        public async Task InitializeAsync()
        {
            await _testHelper.DeleteAllExportJobRecordsAsync();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenANewExportRequest_WhenCreatingExportJob_ThenGetsJobCreated()
        {
            var jobRecord = new ExportJobRecord(_exportRequest.RequestUri, _exportRequest.ResourceType, "hash");

            ExportJobOutcome outcome = await _operationDataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            Assert.NotNull(outcome);
            Assert.NotNull(outcome.JobRecord);
            Assert.NotEmpty(outcome.JobRecord.Id);
            Assert.NotNull(outcome.ETag);
        }

        [Fact]
        public async Task GivenAMatchingJob_WhenGettingById_ThenTheMatchingJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(jobRecord.Id, outcome?.JobRecord?.Id);
        }

        [Fact]
        public async Task GivenNoMatchingJob_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync("test", CancellationToken.None));
        }

        [Fact]
        public async Task GivenAMatchingJob_WhenGettingByHash_ThenTheMatchingJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByHashAsync(jobRecord.Hash, CancellationToken.None);

            Assert.Equal(jobRecord.Id, outcome?.JobRecord?.Id);
        }

        [Fact]
        public async Task GivenNoMatchingJob_WhenGettingByHash_ThenNoMatchingJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByHashAsync("test", CancellationToken.None);

            Assert.Null(outcome);
        }

        [Fact]
        public async Task GivenThereIsNoRunningJob_WhenAcquiringJobs_ThenAvailableJobsShouldBeReturned()
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

        [Theory]
        [InlineData(OperationStatus.Canceled)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenJobIsNotInQueuedState_WhenAcquiringJobs_ThenNoJobShouldBeReturned(OperationStatus operationStatus)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync(jr => jr.Status = operationStatus);

            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync();

            Assert.NotNull(jobs);
            Assert.Empty(jobs);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        public async Task GivenNumberOfRunningJobs_WhenAcquiringJobs_ThenAvailableJobsShouldBeReturned(ushort limit, int expectedNumberOfJobsReturned)
        {
            await CreateRunningJob();
            ExportJobRecord jobRecord1 = await InsertNewExportJobRecordAsync(); // Queued
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Canceled);
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Completed);
            ExportJobRecord jobRecord2 = await InsertNewExportJobRecordAsync(); // Queued
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Failed);

            // The running job should not be acquired.
            var expectedJobRecords = new List<ExportJobRecord> { jobRecord1, jobRecord2 };

            IReadOnlyCollection<ExportJobOutcome> acquiredJobOutcomes = await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: limit);

            Assert.NotNull(acquiredJobOutcomes);
            Assert.Equal(expectedNumberOfJobsReturned, acquiredJobOutcomes.Count);

            foreach (ExportJobOutcome acquiredJobOutcome in acquiredJobOutcomes)
            {
                ExportJobRecord acquiredJobRecord = acquiredJobOutcome.JobRecord;
                ExportJobRecord expectedJobRecord = expectedJobRecords.SingleOrDefault(job => job.Id == acquiredJobRecord.Id);

                Assert.NotNull(expectedJobRecord);

                // The job should be marked as running now since it's acquired.
                expectedJobRecord.Status = OperationStatus.Running;

                ValidateExportJobOutcome(expectedJobRecord, acquiredJobRecord);
            }
        }

        [Fact]
        public async Task GivenThereIsRunningJobThatExpired_WhenAcquiringJobs_ThenTheExpiredJobShouldBeReturned()
        {
            ExportJobOutcome jobOutcome = await CreateRunningJob();

            await Task.Delay(1200);

            IReadOnlyCollection<ExportJobOutcome> expiredJobs = await AcquireExportJobsAsync(jobHeartbeatTimeoutThreshold: TimeSpan.FromSeconds(1));

            Assert.NotNull(expiredJobs);
            Assert.Collection(
                expiredJobs,
                expiredJobOutcome => ValidateExportJobOutcome(jobOutcome.JobRecord, expiredJobOutcome.JobRecord));
        }

        [Fact]
        public async Task GivenThereAreQueuedJobs_WhenSimultaneouslyAcquiringJobs_ThenCorrectJobsShouldBeReturned()
        {
            ExportJobRecord[] jobRecords = new[]
            {
                await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
                await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
                await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
                await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
            };

            var completionSource = new TaskCompletionSource<bool>();

            Task<IReadOnlyCollection<ExportJobOutcome>>[] tasks = new[]
            {
                WaitAndAcquireExportJobsAsync(),
                WaitAndAcquireExportJobsAsync(),
            };

            completionSource.SetResult(true);

            await Task.WhenAll(tasks);

            // Only 2 jobs should have been acquired in total.
            Assert.Equal(2, tasks.Sum(task => task.Result.Count));

            // Only 1 of the tasks should be fulfilled.
            Assert.Equal(2, tasks[0].Result.Count ^ tasks[1].Result.Count);

            async Task<IReadOnlyCollection<ExportJobOutcome>> WaitAndAcquireExportJobsAsync()
            {
                await completionSource.Task;

                return await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 2);
            }
        }

        [Fact]
        public async Task GivenARunningJob_WhenUpdatingTheJob_ThenTheJobShouldBeUpdated()
        {
            ExportJobOutcome jobOutcome = await CreateRunningJob();
            ExportJobRecord job = jobOutcome.JobRecord;

            job.Status = OperationStatus.Completed;

            await _operationDataStore.UpdateExportJobAsync(job, jobOutcome.ETag, CancellationToken.None);
            ExportJobOutcome updatedJobOutcome = await _operationDataStore.GetExportJobByIdAsync(job.Id, CancellationToken.None);

            ValidateExportJobOutcome(job, updatedJobOutcome?.JobRecord);
        }

        [Fact]
        public async Task GivenAnOldVersionOfAJob_WhenUpdatingTheJob_ThenJobConflictExceptionShouldBeThrown()
        {
            ExportJobOutcome jobOutcome = await CreateRunningJob();
            ExportJobRecord job = jobOutcome.JobRecord;

            // Update the job for a first time. This should not fail.
            job.Status = OperationStatus.Completed;
            WeakETag jobVersion = jobOutcome.ETag;
            await _operationDataStore.UpdateExportJobAsync(job, jobVersion, CancellationToken.None);

            // Attempt to update the job a second time with the old version.
            await Assert.ThrowsAsync<JobConflictException>(() => _operationDataStore.UpdateExportJobAsync(job, jobVersion, CancellationToken.None));
        }

        [Fact]
        public async Task GivenANonexistentJob_WhenUpdatingTheJob_ThenJobNotFoundExceptionShouldBeThrown()
        {
            ExportJobOutcome jobOutcome = await CreateRunningJob();

            ExportJobRecord job = jobOutcome.JobRecord;
            WeakETag jobVersion = jobOutcome.ETag;

            await _testHelper.DeleteExportJobRecordAsync(job.Id);

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.UpdateExportJobAsync(job, jobVersion, CancellationToken.None));
        }

        [Fact]
        public async Task GivenThereIsARunningJob_WhenSimultaneousUpdateCallsOccur_ThenJobConflictExceptionShouldBeThrown()
        {
            ExportJobOutcome runningJobOutcome = await CreateRunningJob();

            var completionSource = new TaskCompletionSource<bool>();

            Task<ExportJobOutcome>[] tasks = new[]
            {
                WaitAndUpdateExportJobAsync(runningJobOutcome),
                WaitAndUpdateExportJobAsync(runningJobOutcome),
                WaitAndUpdateExportJobAsync(runningJobOutcome),
            };

            completionSource.SetResult(true);

            await Assert.ThrowsAsync<JobConflictException>(() => Task.WhenAll(tasks));

            async Task<ExportJobOutcome> WaitAndUpdateExportJobAsync(ExportJobOutcome jobOutcome)
            {
                await completionSource.Task;

                jobOutcome.JobRecord.Status = OperationStatus.Completed;
                return await _operationDataStore.UpdateExportJobAsync(jobOutcome.JobRecord, jobOutcome.ETag, CancellationToken.None);
            }
        }

        private async Task<ExportJobOutcome> CreateRunningJob()
        {
            // Create a queued job.
            await InsertNewExportJobRecordAsync();

            // Acquire the job. This will timestamp it and set it to running.
            IReadOnlyCollection<ExportJobOutcome> jobOutcomes = await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);

            Assert.NotNull(jobOutcomes);
            Assert.Equal(1, jobOutcomes.Count);

            ExportJobOutcome jobOutcome = jobOutcomes.FirstOrDefault();

            Assert.NotNull(jobOutcome);
            Assert.NotNull(jobOutcome.JobRecord);
            Assert.Equal(OperationStatus.Running, jobOutcome.JobRecord.Status);

            return jobOutcome;
        }

        private async Task<ExportJobRecord> InsertNewExportJobRecordAsync(Action<ExportJobRecord> jobRecordCustomizer = null)
        {
            // Generate a unique hash
            var hashObject = new
            {
                _exportRequest.RequestUri,
                Clock.UtcNow,
            };

            string hash = JsonConvert.SerializeObject(hashObject).ComputeHash();

            var jobRecord = new ExportJobRecord(_exportRequest.RequestUri, "Patient", hash);

            jobRecordCustomizer?.Invoke(jobRecord);

            ExportJobOutcome result = await _operationDataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

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
    }
}
