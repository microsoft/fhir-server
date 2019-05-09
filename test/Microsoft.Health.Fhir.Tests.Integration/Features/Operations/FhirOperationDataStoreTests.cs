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
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.KeyVault;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
    public class FhirOperationDataStoreTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private IFhirOperationDataStore _dataStore;
        private IFhirStorageTestHelper _testHelper;
        private ISecretStore _secretStore;
        private Mediator _mediator;

        public FhirOperationDataStoreTests(FhirStorageTestsFixture fixture)
        {
            _dataStore = fixture.OperationDataStore;
            _testHelper = fixture.TestHelper;
            _secretStore = new InMemoryKeyVaultSecretStore();

            var collection = new ServiceCollection();

            collection.AddSingleton(typeof(IRequestHandler<CreateExportRequest, CreateExportResponse>), new CreateExportRequestHandler(_dataStore, _secretStore));
            collection.AddSingleton(typeof(IRequestHandler<GetExportRequest, GetExportResponse>), new GetExportRequestHandler(_dataStore));

            ServiceProvider services = collection.BuildServiceProvider();

            _mediator = new Mediator(type => services.GetService(type));
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
            var requestUri = new Uri("https://localhost/$export");

            CreateExportResponse result = await _mediator.ExportAsync(requestUri, "destinationType", "connectionString");

            Assert.NotNull(result);
            Assert.NotEmpty(result.JobId);
        }

        [Fact]
        public async Task GivenExportJobThatExists_WhenGettingExportStatus_ThenGetsHttpStatuscodeAccepted()
        {
            var requestUri = new Uri("https://localhost/$export");
            var result = await _mediator.ExportAsync(requestUri, "destinationType", "connectionString");

            requestUri = new Uri("https://localhost/_operation/export/" + result.JobId);
            var exportStatus = await _mediator.GetExportStatusAsync(requestUri, result.JobId);

            Assert.Equal(HttpStatusCode.Accepted, exportStatus.StatusCode);
        }

        [Fact]
        public async Task GivenExportJobThatDoesNotExist_WhenGettingExportStatus_ThenJobNotFoundExceptionIsThrown()
        {
            string id = "exportJobId-1234567";
            var requestUri = new Uri("https://localhost/_operation/export/" + id);

            await Assert.ThrowsAsync<JobNotFoundException>(async () => await _mediator.GetExportStatusAsync(requestUri, id));
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
        [InlineData(OperationStatus.Cancelled)]
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
            ExportJobRecord jobRecord1 = await InsertNewExportJobRecordAsync();
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Running);
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Cancelled);
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Completed);
            ExportJobRecord jobRecord2 = await InsertNewExportJobRecordAsync();
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Failed);

            ExportJobRecord[] expectedJobRecords = new[] { jobRecord1, jobRecord2 };

            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: limit);

            Assert.NotNull(jobs);

            Action<ExportJobOutcome>[] validators = expectedJobRecords
                .Take(expectedNumberOfJobsReturned)
                .Select(expectedJobRecord => new Action<ExportJobOutcome>(job =>
                {
                    // The job should be marked as running now since it's acquired.
                    expectedJobRecord.Status = OperationStatus.Running;

                    ValidateExportJobOutcome(expectedJobRecord, job.JobRecord);
                })).ToArray();

            Assert.Collection(
                jobs,
                validators);
        }

        [Fact]
        public async Task GivenThereIsRunningJobThatExpired_WhenAcquiringJobs_ThenTheExpiredJobShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Running);

            await Task.Delay(1200);

            IReadOnlyCollection<ExportJobOutcome> jobs = await AcquireExportJobsAsync(jobHeartbeatTimeoutThreshold: TimeSpan.FromSeconds(1));

            Assert.NotNull(jobs);
            Assert.Collection(
                jobs,
                job => ValidateExportJobOutcome(jobRecord, job.JobRecord));
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

            Task<IReadOnlyCollection<ExportJobOutcome>>[] tasks = new[]
            {
                AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 2),
                AcquireExportJobsAsync(maximumNumberOfConcurrentJobAllowed: 2),
            };

            Parallel.ForEach(tasks, task => Task.Run(async () => { await task; }));

            await Task.WhenAll(tasks);

            // Only 2 jobs should have been acquired in total.
            Assert.Equal(2, tasks.Sum(task => task.Result.Count));

            // Only 1 of the tasks should be fulfilled.
            Assert.Equal(2, tasks[0].Result.Count ^ tasks[1].Result.Count);
        }

        private async Task<ExportJobRecord> InsertNewExportJobRecordAsync(Action<ExportJobRecord> jobRecordCustomizer = null)
        {
            var jobRecord = new ExportJobRecord(new Uri("http://localhost"));

            jobRecordCustomizer?.Invoke(jobRecord);

            await _dataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            return jobRecord;
        }

        private async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(
            ushort maximumNumberOfConcurrentJobAllowed = 1,
            TimeSpan? jobHeartbeatTimeoutThreshold = null)
        {
            if (jobHeartbeatTimeoutThreshold == null)
            {
                jobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(1);
            }

            return await _dataStore.AcquireExportJobsAsync(
                maximumNumberOfConcurrentJobAllowed,
                jobHeartbeatTimeoutThreshold.Value,
                CancellationToken.None);
        }

        private void ValidateExportJobOutcome(ExportJobRecord expected, ExportJobRecord actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.CancelledTime, actual.CancelledTime);
            Assert.Equal(expected.EndTime, actual.EndTime);
            Assert.Equal(expected.Hash, actual.Hash);
            Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
            Assert.Equal(expected.StartTime, actual.StartTime);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.NumberOfConsecutiveFailures, actual.NumberOfConsecutiveFailures);
            Assert.Equal(expected.QueuedTime, actual.QueuedTime);
        }
    }
}
