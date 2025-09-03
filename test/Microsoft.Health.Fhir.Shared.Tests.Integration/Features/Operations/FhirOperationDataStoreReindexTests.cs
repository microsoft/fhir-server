// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.FhirPath.Sprache;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Features.Operations;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Xunit;
using JobConflictException = Microsoft.Health.Fhir.Core.Features.Operations.JobConflictException;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
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
            await CancelActiveReindexJobIfExists();

            GetTestQueueClient().ClearJobs();

            await AssertNoReindexJobsExist();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        private async Task AssertNoReindexJobsExist()
        {
            var jobs = await _operationDataStore.AcquireReindexJobsAsync(10, TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.Empty(jobs);
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
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync();

            // Transition the job to its final state
            await CompleteReindexJobAsync(jobRecord, (JobStatus)operationStatus);

            IReadOnlyCollection<ReindexJobWrapper> jobs = await AcquireReindexJobsAsync();

            Assert.NotNull(jobs);
            Assert.Empty(jobs);
        }

        [Fact]
        public async Task GivenAReindexJobWithQueryErrors_ThenErrorsAreNotDisplayed()
        {
            string queryListError = "An unhandled error has occurred.";
            string resourceType = KnownResourceTypes.Device;
            string errorResult = $"{resourceType}: {queryListError}";

            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync(jobRecord => jobRecord.Status = OperationStatus.Completed);
            ReindexJobWrapper job = await _operationDataStore.GetReindexJobByIdAsync(jobRecord.Id, default);

            var reindexJobQueryStatus = new ReindexJobQueryStatus(resourceType, null)
            {
                Error = queryListError,
            };
            job.JobRecord.QueryList.TryAdd(reindexJobQueryStatus, 1);

            ResourceElement resp = job.ToParametersResourceElement();
            var parms = resp.ResourceInstance as Hl7.Fhir.Model.Parameters;

            // we do not want these errors to be shown to the customer at this point as we can't guarantee
            // that the message won't have sensitive data
            var parm = parms.Parameter.Where(e => e.Name == JobRecordProperties.QueryListErrors).SingleOrDefault();

            Assert.Null(parm);
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
        public async Task GivenARunningReindexJob_WhenUpdatingTheReindexJob_ThenTheJobShouldBeUpdated()
        {
            ReindexJobWrapper jobWrapper = await CreateRunningReindexJob();
            ReindexJobRecord job = jobWrapper.JobRecord;

            job.Status = OperationStatus.Completed;

            await CompleteReindexJobAsync(job, JobStatus.Completed, CancellationToken.None);
            ReindexJobWrapper updatedJobWrapper = await _operationDataStore.GetReindexJobByIdAsync(job.Id, CancellationToken.None);

            ValidateReindexJobRecord(job, updatedJobWrapper?.JobRecord);
        }

        [Fact]
        public async Task GivenANonexistentReindexJob_WhenUpdatingTheReindexJob_ThenJobNotFoundExceptionShouldBeThrown()
        {
            Dictionary<string, string> searchParamHashMap = new Dictionary<string, string>();

            // Create a local job record with a random ID that doesn't exist in the database or queue
            var nonExistentJobRecord = new ReindexJobRecord(searchParamHashMap, new List<string>(), new List<string>(), new List<string>())
            {
                Id = "999999", // Use a non-existent ID
            };

            await Assert.ThrowsAsync<JobNotFoundException>(() => CompleteReindexJobAsync(nonExistentJobRecord, JobStatus.Running, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnActiveReindexJob_WhenGettingActiveReindexJobs_ThenTheCorrectJobIdShouldBeReturned()
        {
            await CancelActiveReindexJobIfExists();
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync();

            (bool, string) activeReindexJobResult = await _operationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);

            Assert.True(activeReindexJobResult.Item1);
            Assert.Equal(jobRecord.Id, activeReindexJobResult.Item2);
        }

        [Theory]
        [InlineData(JobStatus.Completed)]
        [InlineData(JobStatus.Failed)]
        [InlineData(JobStatus.Cancelled)]
        public async Task GivenNoActiveReindexJobs_WhenGettingActiveReindexJobs_ThenNoJobIdShouldBeReturned(JobStatus targetStatus)
        {
            // Create a queued job first
            ReindexJobRecord jobRecord = await InsertNewReindexJobRecordAsync();

            // Transition the job to its final state
            await CompleteReindexJobAsync(jobRecord, targetStatus);

            // Verify the job is now inactive
            (bool found, string id) = await _operationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);

            Assert.False(found);
            Assert.Null(id);
        }

        [Fact]
        public async Task GivenNoReindexJobs_WhenGettingActiveReindexJobs_ThenNoJobIdShouldBeReturned()
        {
            (bool, string) activeReindexJobResult = await _operationDataStore.CheckActiveReindexJobsAsync(CancellationToken.None);

            Assert.False(activeReindexJobResult.Item1);
            Assert.Null(activeReindexJobResult.Item2);
        }

        private async Task<ReindexJobWrapper> CreateRunningReindexJob()
        {
            // Create a queued job.
            await InsertNewReindexJobRecordAsync();

            // Acquire the job. This will timestamp it and set it to running.
            IReadOnlyCollection<ReindexJobWrapper> jobWrappers = await AcquireReindexJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);

            Assert.NotNull(jobWrappers);
            Assert.Single(jobWrappers);

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
            var jobRecord = new ReindexJobRecord(searchParamHashMap, new List<string>(), new List<string>(), new List<string>());

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
            Assert.Equal(expected.StartTime, actual.StartTime);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(
                expected.QueuedTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                actual.QueuedTime.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private async Task CancelActiveReindexJobIfExists(CancellationToken cancellationToken = default)
        {
            var (found, id) = await _operationDataStore.CheckActiveReindexJobsAsync(cancellationToken);
            if (found && !string.IsNullOrEmpty(id))
            {
                var cancelReindexHandler = new CancelReindexRequestHandler(_operationDataStore, DisabledFhirAuthorizationService.Instance);
                await cancelReindexHandler.Handle(new CancelReindexRequest(id), cancellationToken);

                // Optionally, wait for the job to be marked as canceled
                var job = await _operationDataStore.GetReindexJobByIdAsync(id, cancellationToken);
                int attempts = 0;
                while (job.JobRecord.Status != OperationStatus.Canceled && attempts < 5)
                {
                    await Task.Delay(500, cancellationToken);
                    job = await _operationDataStore.GetReindexJobByIdAsync(id, cancellationToken);
                    attempts++;
                }
            }
        }

        private async Task CompleteReindexJobAsync(ReindexJobRecord jobRecord, JobStatus targetStatus, CancellationToken cancellationToken = default)
        {
            var queueClient = GetTestQueueClient();

            // Create JobInfo to transition the job's state
            var jobInfo = new JobInfo
            {
                Id = long.Parse(jobRecord.Id),
                Status = targetStatus,
                QueueType = (byte)QueueType.Reindex,
            };

            // Complete the job with the desired final state
            await queueClient.CompleteJobAsync(jobInfo, false, cancellationToken);
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
