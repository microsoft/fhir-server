// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations
{
    [Collection(FhirOperationTestConstants.FhirOperationTests)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class FhirOperationDataStoreTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly IFhirOperationDataStore _operationDataStore;
        private readonly IFhirStorageTestHelper _testHelper;

        private readonly CreateExportRequest _exportRequest = new CreateExportRequest(new Uri("http://localhost/ExportJob"), "destinationType", "destinationConnection");

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
        public async Task GivenNoMatchingJob_WhenGettingById_ThehJobNotFoundExceptionShouldBeThrown()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetExportJobByIdAsync("test", CancellationToken.None));
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
        public async Task GivenAMatchingJob_WhenGettingByHash_ThenTheMatchingJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.GetExportJobByHashAsync(jobRecord.Hash, CancellationToken.None);

            Assert.Equal(jobRecord.Id, outcome?.JobRecord?.Id);
        }

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
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
            ExportJobRecord jobRecord1 = await InsertNewExportJobRecordAsync(); // Queued
            ExportJobRecord jobRecord2 = await InsertNewExportJobRecordAsync(); // Queued
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Canceled);
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Completed);
            ExportJobRecord jobRecord3 = await InsertNewExportJobRecordAsync(); // Queued
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Failed);

            // Set the one of the queued jobs to running, and update its timestamp. This job shouldn't be returned next time we acquire.
            IReadOnlyCollection<ExportJobOutcome> runningJobs = await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);

            Assert.NotNull(runningJobs);
            Assert.Equal(1, runningJobs.Count);

            ExportJobOutcome runningJob = runningJobs.FirstOrDefault();

            Assert.NotNull(runningJob);

            var expectedJobRecords = new List<ExportJobRecord> { jobRecord1, jobRecord2, jobRecord3 };

            // Remove the running job from the list of jobs that are expected to be returned next acquire.
            ExportJobRecord runningJobRecord = expectedJobRecords.SingleOrDefault(job => job.Id == runningJob.JobRecord.Id);
            expectedJobRecords.Remove(runningJobRecord);

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
            // Create a job and set it to running.
            await InsertNewExportJobRecordAsync();
            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);

            ExportJobOutcome jobOutcome = jobs.First();

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

        private async Task<ExportJobRecord> InsertNewExportJobRecordAsync(Action<ExportJobRecord> jobRecordCustomizer = null)
        {
            var jobRecord = new ExportJobRecord(_exportRequest.RequestUri, "Patient", "hash");

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
