// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

////using System;
////using System.Threading;
////using System.Threading.Tasks;
////using Microsoft.Extensions.Logging.Abstractions;
////using Microsoft.Health.Fhir.Core.Features.Operations;
////using Microsoft.Health.Fhir.Core.Features.Operations.Export;
////using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
////using Microsoft.Health.Fhir.Core.Features.SecretStore;
////using Microsoft.Health.Fhir.Core.Messages.Export;
////using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
////using Microsoft.Health.Fhir.Tests.Integration.Persistence;
////using NSubstitute;
////using Xunit;

////namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Export
////{
////    [Collection(FhirOperationTestConstants.FhirOperationTests)]
////    [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
////    public class ExportJobTaskTests : IClassFixture<FhirStorageTestsFixture>
////    {
////        private readonly IFhirOperationDataStore _fhirOperationDataStore;
////        private readonly ISecretStore _secretStore;
////        private readonly IExportExecutor _exportExecutor;
////        private readonly ExportJobTask _exportJobTask;

////        public ExportJobTaskTests(FhirStorageTestsFixture fixture)
////        {
////            _fhirOperationDataStore = fixture.OperationDataStore;
////            _secretStore = Substitute.For<ISecretStore>();
////            _exportExecutor = Substitute.For<IExportExecutor>();

////            _exportJobTask = new ExportJobTask(_fhirOperationDataStore, _secretStore, _exportExecutor, NullLogger<ExportJobTask>.Instance);
////        }

////        [Fact]
////        public async Task GivenAnExportJobRecord_WhenExecuted_ThenTheExportJobRecordShouldBeUpdated()
////        {
////            ExportJobOutcome job = await CreateAndExecuteCreateExportJobAsync();

////            await _exportJobTask.ExecuteAsync(job.JobRecord, job.ETag, CancellationToken.None);

////            ExportJobOutcome actual = await _fhirOperationDataStore.GetExportJobByIdAsync(job.JobRecord.Id, CancellationToken.None);

////            Assert.NotNull(actual);
////            Assert.Equal(OperationStatus.Completed, actual.JobRecord.Status);
////        }

////        [Fact]
////        public async Task GivenAnExportJobRecordThatWasUpdated_WhenExecuted_ThenTheExportJobRecordShouldBeUpdated()
////        {
////            ExportJobOutcome initialJob = await CreateAndExecuteCreateExportJobAsync();

////            // Update the job to be canceled.
////            ExportJobRecord jobRecord = initialJob.JobRecord;

////            jobRecord.Status = OperationStatus.Cancelled;

////            ExportJobOutcome updatedJob = await _fhirOperationDataStore.UpdateExportJobAsync(jobRecord, initialJob.ETag, CancellationToken.None);

////            // Create a new task with the old ETag.
////            await _exportJobTask.ExecuteAsync(initialJob.JobRecord, initialJob.ETag, CancellationToken.None);

////            ExportJobOutcome actual = await _fhirOperationDataStore.GetExportJobByIdAsync(jobRecord.Id, CancellationToken.None);

////            Assert.NotNull(actual);

////            // The job should remain canceled since it shouldn't have been able to acquire the job.
////            Assert.Equal(OperationStatus.Cancelled, actual.JobRecord.Status);
////        }

////        private async Task<ExportJobOutcome> CreateAndExecuteCreateExportJobAsync()
////        {
////            var jobRecord = new ExportJobRecord(new CreateExportRequest(new Uri("https://localhost/ExportJob"), "destinationType", "destinationConnection"), "hash");

////            return await _fhirOperationDataStore.CreateExportJobAsync(jobRecord, CancellationToken.None);
////        }
////    }
////}
