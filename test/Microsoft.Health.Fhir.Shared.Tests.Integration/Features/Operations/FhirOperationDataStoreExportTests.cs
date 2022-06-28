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
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
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
    public class FhirOperationDataStoreExportTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly IFhirOperationDataStore _operationDataStore;
        private readonly IFhirStorageTestHelper _testHelper;
        private readonly FhirStorageTestsFixture _fixture;

        private readonly CreateExportRequest _exportRequest = new CreateExportRequest(new Uri("http://localhost/ExportJob"), ExportJobType.All);

        public FhirOperationDataStoreExportTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
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
        [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
        public async Task ReturnExportRegisteredInOldSchema()
        {
            var jobRecord = new ExportJobRecord(_exportRequest.RequestUri, _exportRequest.RequestType, ExportFormatTags.ResourceName, _exportRequest.ResourceType, null, "hash", rollingFileSizeInMB: 64);
            var raw = JsonConvert.SerializeObject(jobRecord);
            var jobId = jobRecord.Id;
            await _fixture.SqlHelper.ExecuteSqlCmd("INSERT INTO dbo.ExportJob (Id, Hash, Status, RawJobRecord) SELECT '" + jobId + "', 'test', 'Queued', '" + raw + "'");
            var outcome = await _operationDataStore.GetExportJobByIdAsync(jobId, CancellationToken.None);
            Assert.NotNull(outcome);
            Assert.Equal(jobId, outcome.JobRecord.Id);
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
        public async Task GivenAMatchingExportJob_WhenReCreatingPreviouslyCreatedJob_ThenTheMatchingJobShouldBeReturned()
        {
            var jobRecord = await InsertNewExportJobRecordAsync();

            ExportJobOutcome outcome = await _operationDataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            Assert.Equal(jobRecord.Id, outcome?.JobRecord?.Id);
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

        [Theory]
        [InlineData(OperationStatus.Canceled, 0)]
        [InlineData(OperationStatus.Completed, 0)]
        [InlineData(OperationStatus.Failed, 0)]
        [InlineData(OperationStatus.Queued, 1)]
        public async Task GivenExportJobIsNotInQueuedState_WhenAcquiringExportJobs_ThenNoExportJobShouldBeReturned(OperationStatus operationStatus, int expectedNumberOfJobsReturned)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync(jr => jr.Status = operationStatus);

            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync();

            Assert.NotNull(jobs);
            Assert.Equal(expectedNumberOfJobsReturned, jobs.Count);
        }

        [Theory]
        ////TODO: Revise below when per instance max is introduced.
        [InlineData(1, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        public async Task GivenNumberOfRunningExportJobs_WhenAcquiringExportJobs_ThenAvailableExportJobsShouldBeReturned(ushort limit, int expectedNumberOfJobsReturned)
        {
            await CreateRunningExportJob();
            await InsertNewExportJobRecordAsync(jr =>
            {
                jr.Status = OperationStatus.Canceled;
                jr.RequestUri = new Uri(jr.RequestUri.ToString() + "1");
            });
            await InsertNewExportJobRecordAsync(jr =>
            {
                jr.Status = OperationStatus.Completed;
                jr.RequestUri = new Uri(jr.RequestUri.ToString() + "2");
            });
            await InsertNewExportJobRecordAsync(jr =>
            {
                jr.Status = OperationStatus.Failed;
                jr.RequestUri = new Uri(jr.RequestUri.ToString() + "3");
            });
            ExportJobRecord jobRecord1 = await InsertNewExportJobRecordAsync(jr => jr.RequestUri = new Uri(jr.RequestUri.ToString() + "4")); // Queued
            ExportJobRecord jobRecord2 = await InsertNewExportJobRecordAsync(jr => jr.RequestUri = new Uri(jr.RequestUri.ToString() + "5")); // Queued

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

        [Fact]
        [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)] // TODO: Adopt for SQL when checked in.
        public async Task GivenThereAreQueuedExportJobs_WhenSimultaneouslyAcquiringExportJobs_ThenCorrectExportJobsShouldBeReturned()
        {
            ExportJobRecord[] jobRecords = new[]
            {
                await InsertNewExportJobRecordAsync(jr =>
                {
                    jr.Status = OperationStatus.Queued;
                    jr.RequestUri = new Uri(jr.RequestUri.ToString() + "1");
                }),
                await InsertNewExportJobRecordAsync(jr =>
                {
                    jr.Status = OperationStatus.Queued;
                    jr.RequestUri = new Uri(jr.RequestUri.ToString() + "2");
                }),
                await InsertNewExportJobRecordAsync(jr =>
                {
                    jr.Status = OperationStatus.Queued;
                    jr.RequestUri = new Uri(jr.RequestUri.ToString() + "3");
                }),
                await InsertNewExportJobRecordAsync(jr =>
                {
                    jr.Status = OperationStatus.Queued;
                    jr.RequestUri = new Uri(jr.RequestUri.ToString() + "4");
                }),
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

            // Both of the results should be fullilled since we requested 1 job twice and we can run jobs in parallel.
            // This checks that the results are the same.
            Assert.Equal(0, tasks[0].Result.Count ^ tasks[1].Result.Count);

            async Task<IReadOnlyCollection<ExportJobOutcome>> WaitAndAcquireExportJobsAsync()
            {
                await completionSource.Task;

                return await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 1);
            }
        }

        [Fact]
        public async Task GivenARunningExportJob_WhenUpdatingTheExportJob_ThenTheExportJobShouldBeUpdated()
        {
            ExportJobOutcome jobOutcome = await CreateRunningExportJob();
            ExportJobRecord job = jobOutcome.JobRecord;

            job.Status = OperationStatus.Completed;

            await _operationDataStore.UpdateExportJobAsync(job, jobOutcome.ETag, CancellationToken.None);
            ExportJobOutcome updatedJobOutcome = await _operationDataStore.GetExportJobByIdAsync(job.Id, CancellationToken.None);

            ValidateExportJobOutcome(job, updatedJobOutcome?.JobRecord);
        }

        [Fact]
        public async Task GivenAnOldVersionOfAnExportJob_WhenUpdatingTheExportJob_ThenJobConflictExceptionShouldBeThrown()
        {
            ExportJobOutcome jobOutcome = await CreateRunningExportJob();
            ExportJobRecord job = jobOutcome.JobRecord;

            // Update the job for a first time. This should not fail.
            job.Status = OperationStatus.Completed;
            WeakETag jobVersion = jobOutcome.ETag;
            await _operationDataStore.UpdateExportJobAsync(job, jobVersion, CancellationToken.None);

            // Attempt to update the job a second time with the old version.
            await Assert.ThrowsAsync<JobConflictException>(() => _operationDataStore.UpdateExportJobAsync(job, jobVersion, CancellationToken.None));
        }

        [Fact]
        public async Task GivenANonexistentExportJob_WhenUpdatingTheExportJob_ThenJobNotFoundExceptionShouldBeThrown()
        {
            ExportJobOutcome jobOutcome = await CreateRunningExportJob();

            ExportJobRecord job = jobOutcome.JobRecord;
            WeakETag jobVersion = jobOutcome.ETag;

            await _testHelper.DeleteExportJobRecordAsync(job.Id);

            await Assert.ThrowsAsync<JobNotFoundException>(() => _operationDataStore.UpdateExportJobAsync(job, jobVersion, CancellationToken.None));
        }

        [Fact]
        public async Task GivenThereIsARunningExportJob_WhenSimultaneousUpdateCallsOccur_ThenJobConflictExceptionShouldBeThrown()
        {
            ExportJobOutcome runningJobOutcome = await CreateRunningExportJob();

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

        private async Task<ExportJobOutcome> CreateRunningExportJob()
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
    }
}
