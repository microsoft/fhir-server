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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
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
    }
}
