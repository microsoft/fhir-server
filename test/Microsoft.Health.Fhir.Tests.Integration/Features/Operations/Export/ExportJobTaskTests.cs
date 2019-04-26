// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Export
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
    public class ExportJobTaskTests : IClassFixture<FhirStorageTestsFixture>
    {
        private IFhirOperationsDataStore _dataStore;

        public ExportJobTaskTests(FhirStorageTestsFixture fixture)
        {
            _dataStore = fixture.OperationsDataStore;
        }

        [Fact]
        public async Task GivenAnExportJobRecord_WhenExecuted_ThenTheExportJobRecordShouldBeUpdated()
        {
            var jobRecord = new ExportJobRecord(new Uri("https://localhost/ExportJob"));

            ExportJobOutcome job = await _dataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            var task = new ExportJobTask(job.JobRecord, job.ETag, _dataStore, NullLogger<ExportJobTask>.Instance);

            await task.ExecuteAsync(CancellationToken.None);

            ExportJobOutcome actual = await _dataStore.GetExportJobAsync(jobRecord.Id, CancellationToken.None);

            Assert.NotNull(actual);
            Assert.Equal(OperationStatus.Completed, actual.JobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportJobRecordThatWasUpdated_WhenExecuted_ThenTheExportJobRecordShouldBeUpdated()
        {
            var jobRecord = new ExportJobRecord(new Uri("https://localhost/ExportJob"));

            ExportJobOutcome initialJob = await _dataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);

            // Update the job to be canceled.
            jobRecord.Status = OperationStatus.Cancelled;

            ExportJobOutcome updatedJob = await _dataStore.UpdateExportJobAsync(jobRecord, initialJob.ETag, CancellationToken.None);

            // Create a new task with the old ETag.
            var task = new ExportJobTask(initialJob.JobRecord, initialJob.ETag, _dataStore, NullLogger<ExportJobTask>.Instance);

            await task.ExecuteAsync(CancellationToken.None);

            ExportJobOutcome actual = await _dataStore.GetExportJobAsync(jobRecord.Id, CancellationToken.None);

            Assert.NotNull(actual);

            // The job should remain canceled since it shouldn't have been able to acquire the job.
            Assert.Equal(OperationStatus.Cancelled, actual.JobRecord.Status);
        }
    }
}
