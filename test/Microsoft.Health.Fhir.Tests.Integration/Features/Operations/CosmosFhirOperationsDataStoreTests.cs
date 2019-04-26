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
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
    public class CosmosFhirOperationsDataStoreTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private IFhirOperationsDataStore _dataStore;
        private IFhirStorageTestHelper _testHelper;
        private Mediator _mediator;

        public CosmosFhirOperationsDataStoreTests(FhirStorageTestsFixture fixture)
        {
            _dataStore = fixture.OperationsDataStore;
            _testHelper = fixture.TestHelper;

            var collection = new ServiceCollection();

            collection.AddSingleton(typeof(IRequestHandler<CreateExportRequest, CreateExportResponse>), new CreateExportRequestHandler(_dataStore));
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

            CreateExportResponse result = await _mediator.ExportAsync(requestUri);

            Assert.NotNull(result);
            Assert.NotEmpty(result.JobId);
        }

        [Fact]
        public async Task GivenExportJobThatExists_WhenGettingExportStatus_ThenGetsHttpStatuscodeAccepted()
        {
            var requestUri = new Uri("https://localhost/$export");
            var result = await _mediator.ExportAsync(requestUri);

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
        public async Task GivenThereIsNoRunningJob_WhenAvailableJobsAreRequested_ThenAvailableJobsShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync();

            IReadOnlyCollection<ExportJobOutcome> jobs = await GetAvailableExportJobRecordsAsync();

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
        public async Task GivenJobIsNotInQueuedState_WhenAvailableJobsAreRequested_ThenNoJobShouldBeReturned(OperationStatus operationStatus)
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync(jr => jr.Status = operationStatus);

            IReadOnlyCollection<ExportJobOutcome> jobs = await GetAvailableExportJobRecordsAsync();

            Assert.NotNull(jobs);
            Assert.Empty(jobs);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        public async Task GivenNumberOfRunningJobs_WhenAvailableJobsAreRequested_ThenAvailableJobsShouldBeReturned(ushort limit, int expectedNumberOfJobsReturned)
        {
            ExportJobRecord jobRecord1 = await InsertNewExportJobRecordAsync();
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Running);
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Cancelled);
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Completed);
            ExportJobRecord jobRecord2 = await InsertNewExportJobRecordAsync();
            await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Failed);

            ExportJobRecord[] expectedJobRecords = new[] { jobRecord1, jobRecord2 };

            IReadOnlyCollection<ExportJobOutcome> jobs = await GetAvailableExportJobRecordsAsync(maximumNumberOfConcurrentJobAllowed: limit);

            Assert.NotNull(jobs);

            Action<ExportJobOutcome>[] validators = expectedJobRecords
                .Take(expectedNumberOfJobsReturned)
                .Select(expectedJobRecord => new Action<ExportJobOutcome>(job => ValidateExportJobOutcome(expectedJobRecord, job.JobRecord))).ToArray();

            Assert.Collection(
                jobs,
                validators);
        }

        [Fact]
        public async Task GivenThereIsRunningJobThatExpired_WhenAvailableJobsAreRequested_ThenTheExpiredJobShouldBeReturned()
        {
            ExportJobRecord jobRecord = await InsertNewExportJobRecordAsync(jr => jr.Status = OperationStatus.Running);

            await Task.Delay(1200);

            IReadOnlyCollection<ExportJobOutcome> jobs = await GetAvailableExportJobRecordsAsync(jobHeartbeatTimeoutThreshold: TimeSpan.FromSeconds(1));

            Assert.NotNull(jobs);
            Assert.Collection(
                jobs,
                job => ValidateExportJobOutcome(jobRecord, job.JobRecord));
        }

        private async Task<ExportJobRecord> InsertNewExportJobRecordAsync(Action<ExportJobRecord> jobRecordCustomizer = null)
        {
            var jobRecord = new ExportJobRecord(new Uri("http://localhost"));

            jobRecordCustomizer?.Invoke(jobRecord);

            await _dataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            return jobRecord;
        }

        private async Task<IReadOnlyCollection<ExportJobOutcome>> GetAvailableExportJobRecordsAsync(
            ushort maximumNumberOfConcurrentJobAllowed = 1,
            TimeSpan? jobHeartbeatTimeoutThreshold = null)
        {
            if (jobHeartbeatTimeoutThreshold == null)
            {
                jobHeartbeatTimeoutThreshold = TimeSpan.FromMinutes(1);
            }

            return await _dataStore.GetAvailableExportJobsAsync(
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
