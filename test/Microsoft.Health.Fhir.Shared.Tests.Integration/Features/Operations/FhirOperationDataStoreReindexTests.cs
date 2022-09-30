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
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Features.Operations;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Index)]
    [Collection(FhirOperationTestConstants.FhirOperationTests)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class FhirOperationDataStoreReindexTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly IFhirOperationDataStore _operationDataStore;
        private readonly IFhirStorageTestHelper _testHelper;

        public FhirOperationDataStoreReindexTests(FhirStorageTestsFixture fixture)
        {
            _operationDataStore = fixture.OperationDataStore;
            _testHelper = fixture.TestHelper;
        }

        public async Task InitializeAsync()
        {
            await _testHelper.DeleteAllReindexJobRecordsAsync();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenAMatchingReindexJob_WhenGettingById_ThenTheMatchingReindexJobShouldBeReturned()
        {
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync();

            ReindexJobWrapper jobWrapper = await _operationDataStore.GetReindexJobByIdAsync(jobRecord.Id, CancellationToken.None);

            Assert.Equal(jobRecord.Id, jobWrapper?.JobRecord?.Id);
        }

        [Fact]
        public async Task GivenNoMatchingReindexJob_WhenGettingById_ThenJobNotFoundExceptionShouldBeThrown()
        {
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync();

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.GetReindexJobByIdAsync("test", CancellationToken.None));
        }

        [Fact]
        public async Task GivenThereIsNoRunningReindexJob_WhenAcquiringReindexJobs_ThenAvailableReindexJobsShouldBeReturned()
        {
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync();

            IReadOnlyCollection<ReindexJobWrapper> jobs = await AcquireReindexJobsAsync();

            // The job should be marked as running now since it's acquired.
            jobRecord.Status = OperationStatus.Running;

            Assert.NotNull(jobs);
            Assert.Collection(
                jobs,
                job => ValidateReindexJobRecord(jobRecord, job.JobRecord));
        }

        [Theory]
        [InlineData(OperationStatus.Canceled)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenNoReindexJobInQueuedState_WhenAcquiringReindexJobs_ThenNoReindexJobShouldBeReturned(OperationStatus operationStatus)
        {
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync(jobRecord => jobRecord.Status = operationStatus);

            IReadOnlyCollection<ReindexJobWrapper> jobs = await AcquireReindexJobsAsync();

            Assert.NotNull(jobs);
            Assert.Empty(jobs);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        public async Task GivenNumberOfRunningReindexJobs_WhenAcquiringReindexJobs_ThenAvailableReindexJobsShouldBeReturned(ushort limit, int expectedNumberOfJobsReturned)
        {
            await CreateRunningReindexJob();
            ReindexJobRecord jobRecord1 = await InsertNewReindexJobRecordAsync(); // Queued
            await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Canceled);
            await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Completed);
            ReindexJobRecord jobRecord2 = await InsertNewReindexJobRecordAsync(); // Queued
            await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Failed);

            // The jobs that are running or completed should not be acquired.
            var expectedJobRecords = new List<ReindexJobRecord> { jobRecord1, jobRecord2 };

            IReadOnlyCollection<ReindexJobWrapper> acquiredJobWrappers = await AcquireReindexJobsAsync(maximumNumberOfConcurrentJobAllowed: limit);

            Assert.NotNull(acquiredJobWrappers);
            Assert.Equal(expectedNumberOfJobsReturned, acquiredJobWrappers.Count);

            foreach (ReindexJobWrapper acquiredJobWrapper in acquiredJobWrappers)
            {
                ReindexJobRecord acquiredJobRecord = acquiredJobWrapper.JobRecord;
                ReindexJobRecord expectedJobRecord = expectedJobRecords.SingleOrDefault(job => job.Id == acquiredJobRecord.Id);

                Assert.NotNull(expectedJobRecord);

                // The job should be marked as running now since it's acquired.
                expectedJobRecord.Status = OperationStatus.Running;

                ValidateReindexJobRecord(expectedJobRecord, acquiredJobRecord);
            }
        }

        [Fact]
        public async Task GivenThereIsRunningReindexJobThatExpired_WhenAcquiringReindexJobs_ThenTheExpiredReindexJobShouldBeReturned()
        {
            ReindexJobWrapper jobWrapper = await CreateRunningReindexJob();

            await Task.Delay(1200);

            IReadOnlyCollection<ReindexJobWrapper> expiredJobs = await AcquireReindexJobsAsync(jobHeartbeatTimeoutThreshold: TimeSpan.FromSeconds(1));

            Assert.NotNull(expiredJobs);
            Assert.Collection(
                expiredJobs,
                expiredJobWrapper => ValidateReindexJobRecord(jobWrapper.JobRecord, expiredJobWrapper.JobRecord));
        }

        [Fact]
        public async Task GivenThereAreQueuedReindexJobs_WhenSimultaneouslyAcquiringReindexJobs_ThenCorrectNumberOfReindexJobsShouldBeReturned()
        {
            ReindexJobRecord[] jobRecords = new[]
            {
                await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
                await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
                await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
                await InsertNewReindexJobRecordAsync(jr => jr.Status = OperationStatus.Queued),
            };

            var completionSource = new TaskCompletionSource<bool>();

            Task<IReadOnlyCollection<ReindexJobWrapper>>[] tasks = new[]
            {
                WaitAndAcquireReindexJobsAsync(),
                WaitAndAcquireReindexJobsAsync(),
            };

            completionSource.SetResult(true);

            await Task.WhenAll(tasks);

            // Only 2 jobs should have been acquired in total.
            Assert.Equal(2, tasks.Sum(task => task.Result.Count));

            // Only 1 of the tasks should be fulfilled.
            Assert.Equal(2, tasks[0].Result.Count ^ tasks[1].Result.Count);

            async Task<IReadOnlyCollection<ReindexJobWrapper>> WaitAndAcquireReindexJobsAsync()
            {
                await completionSource.Task;

                return await AcquireReindexJobsAsync(maximumNumberOfConcurrentJobAllowed: 2);
            }
        }

        [Fact]
        public async Task GivenARunningReindexJob_WhenUpdatingTheReindexJob_ThenTheJobShouldBeUpdated()
        {
            ReindexJobWrapper jobWrapper = await CreateRunningReindexJob();
            ReindexJobRecord job = jobWrapper.JobRecord;

            job.Status = OperationStatus.Completed;

            await _operationDataStore.UpdateReindexJobAsync(job, jobWrapper.ETag, CancellationToken.None);
            ReindexJobWrapper updatedJobWrapper = await _operationDataStore.GetReindexJobByIdAsync(job.Id, CancellationToken.None);

            ValidateReindexJobRecord(job, updatedJobWrapper?.JobRecord);
        }

        [Fact]
        public async Task GivenAnOldVersionOfAReindexJob_WhenUpdatingTheReindexJob_ThenJobConflictExceptionShouldBeThrown()
        {
            ReindexJobWrapper jobWrapper = await CreateRunningReindexJob();
            ReindexJobRecord job = jobWrapper.JobRecord;

            // Update the job for a first time. This should not fail.
            job.Status = OperationStatus.Completed;
            WeakETag jobVersion = jobWrapper.ETag;
            await _operationDataStore.UpdateReindexJobAsync(job, jobVersion, CancellationToken.None);

            // Attempt to update the job a second time with the old version.
            await Assert.ThrowsAsync<JobConflictException>(() => _operationDataStore.UpdateReindexJobAsync(job, jobVersion, CancellationToken.None));
        }

        [Fact]
        public async Task GivenANonexistentReindexJob_WhenUpdatingTheReindexJob_ThenJobNotFoundExceptionShouldBeThrown()
        {
            ReindexJobWrapper jobWrapper = await CreateRunningReindexJob();

            ReindexJobRecord job = jobWrapper.JobRecord;
            WeakETag jobVersion = jobWrapper.ETag;

            await _testHelper.DeleteReindexJobRecordAsync(job.Id);

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.UpdateReindexJobAsync(job, jobVersion, CancellationToken.None));
        }

        [Fact]
        public async Task GivenThereIsARunningReindexJob_WhenSimultaneousUpdateCallsOccur_ThenJobConflictExceptionShouldBeThrown()
        {
            ReindexJobWrapper runningJobWrapper = await CreateRunningReindexJob();

            var completionSource = new TaskCompletionSource<bool>();

            Task<ReindexJobWrapper>[] tasks = new[]
            {
                WaitAndUpdateReindexJobAsync(runningJobWrapper),
                WaitAndUpdateReindexJobAsync(runningJobWrapper),
                WaitAndUpdateReindexJobAsync(runningJobWrapper),
            };

            completionSource.SetResult(true);

            await Assert.ThrowsAsync<JobConflictException>(() => Task.WhenAll(tasks));

            async Task<ReindexJobWrapper> WaitAndUpdateReindexJobAsync(ReindexJobWrapper jobWrapper)
            {
                await completionSource.Task;

                jobWrapper.JobRecord.Status = OperationStatus.Completed;
                return await _operationDataStore.UpdateReindexJobAsync(jobWrapper.JobRecord, jobWrapper.ETag, CancellationToken.None);
            }
        }

        [Theory]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Paused)]
        public async Task GivenAnActiveReindexJob_WhenGettingActiveReindexJobs_ThenTheCorrectJobIdShouldBeReturned(OperationStatus operationStatus)
        {
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync(job => job.Status = operationStatus);

            (bool, string) activeReindexJobResult = await _operationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);

            Assert.True(activeReindexJobResult.Item1);
            Assert.Equal(jobRecord.Id, activeReindexJobResult.Item2);
        }

        [Theory]
        [InlineData(OperationStatus.Canceled)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        [InlineData(OperationStatus.Unknown)]
        public async Task GivenNoActiveReindexJobs_WhenGettingActiveReindexJobs_ThenNoJobIdShouldBeReturned(OperationStatus operationStatus)
        {
            await InsertNewReindexJobRecordAsync(job => job.Status = operationStatus);

            (bool, string) activeReindexJobResult = await _operationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);

            Assert.False(activeReindexJobResult.Item1);
            Assert.Empty(activeReindexJobResult.Item2);
        }

        [Fact]
        public async Task GivenNoReindexJobs_WhenGettingActiveReindexJobs_ThenNoJobIdShouldBeReturned()
        {
            (bool, string) activeReindexJobResult = await _operationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);

            Assert.False(activeReindexJobResult.Item1);
            Assert.Empty(activeReindexJobResult.Item2);
        }

        private async Task<ReindexJobWrapper> CreateRunningReindexJob()
        {
            // Create a queued job.
            await InsertNewReindexJobRecordAsync();

            // Acquire the job. This will timestamp it and set it to running.
            IReadOnlyCollection<ReindexJobWrapper> jobWrappers = await AcquireReindexJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);

            Assert.NotNull(jobWrappers);
            Assert.Equal(1, jobWrappers.Count);

            ReindexJobWrapper jobWrapper = jobWrappers.FirstOrDefault();

            Assert.NotNull(jobWrapper);
            Assert.NotNull(jobWrapper.JobRecord);
            Assert.Equal(OperationStatus.Running, jobWrapper.JobRecord.Status);

            return jobWrapper;
        }

        private async Task<ReindexJobRecord> InsertNewReindexJobRecordAsync(Action<ReindexJobRecord> jobRecordCustomizer = null)
        {
            Dictionary<string, string> searchParamHashMap = new Dictionary<string, string>();
            searchParamHashMap.Add("Patient", "searchParamHash");
            var jobRecord = new ReindexJobRecord(searchParamHashMap, new List<string>(), maxiumumConcurrency: 1);

            jobRecordCustomizer?.Invoke(jobRecord);

            ReindexJobWrapper result = await _operationDataStore.CreateReindexJobAsync(jobRecord, CancellationToken.None);

            return result.JobRecord;
        }

        private async Task<IReadOnlyCollection<ReindexJobWrapper>> AcquireReindexJobsAsync(
            ushort maximumNumberOfConcurrentJobAllowed = 1,
            TimeSpan? jobHeartbeatTimeoutThreshold = null)
        {
            if (jobHeartbeatTimeoutThreshold == null)
            {
                jobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(1);
            }

            return await _operationDataStore.AcquireReindexJobsAsync(
                maximumNumberOfConcurrentJobAllowed,
                jobHeartbeatTimeoutThreshold.Value,
                CancellationToken.None);
        }

        private void ValidateReindexJobRecord(ReindexJobRecord expected, ReindexJobRecord actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.CanceledTime, actual.CanceledTime);
            Assert.Equal(expected.EndTime, actual.EndTime);
            Assert.Equal(expected.ResourceTypeSearchParameterHashMap, actual.ResourceTypeSearchParameterHashMap);
            Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
            Assert.Equal(expected.StartTime, actual.StartTime);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.QueuedTime, actual.QueuedTime);
        }
    }
}
