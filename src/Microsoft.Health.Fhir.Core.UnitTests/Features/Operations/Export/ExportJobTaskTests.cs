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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class ExportJobTaskTests
    {
        private static readonly ExportJobRecord _exportJobRecord = new ExportJobRecord(new Uri("https://localhost/ExportJob/"));
        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();

        private ExportJobTask _exportJobTask;

        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private CancellationToken _cancellationToken;

        public ExportJobTaskTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            _fhirOperationDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, _cancellationToken).Returns(x => new ExportJobOutcome(_exportJobRecord, _weakETag));

            _exportJobTask = new ExportJobTask(
                _exportJobRecord,
                _weakETag,
                _fhirOperationDataStore,
                NullLogger<ExportJobTask>.Instance);
        }

        [Fact]
        public async Task GivenANewJob_WhenExecuted_ThenStatusShouldBeUpdatedToRunning()
        {
            bool capturedStatusUpdate = false;

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Is<ExportJobRecord>(jr => jr.Status == OperationStatus.Running), _weakETag, _cancellationToken).Returns(_ =>
            {
                capturedStatusUpdate = true;

                return new ExportJobOutcome(_exportJobRecord, _weakETag);
            });

            await _exportJobTask.ExecuteAsync(_cancellationToken);

            Assert.True(capturedStatusUpdate);
        }

        [Fact]
        public async Task GivenAJobCompletedSuccessfully_WhenExecuted_ThenStatusShouldBeUpdatedToCompleted()
        {
            var newETag = WeakETag.FromVersionId("1");

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Is<ExportJobRecord>(jr => jr.Status == OperationStatus.Running), _weakETag, _cancellationToken).Returns(_ =>
            {
                return new ExportJobOutcome(_exportJobRecord, newETag);
            });

            bool capturedStatusUpdate = false;

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Is<ExportJobRecord>(jr => jr.Status == OperationStatus.Completed), newETag, _cancellationToken).Returns(_ =>
            {
                capturedStatusUpdate = true;

                return new ExportJobOutcome(_exportJobRecord, _weakETag);
            });

            await _exportJobTask.ExecuteAsync(_cancellationToken);

            Assert.True(capturedStatusUpdate);
            Assert.Equal(OperationStatus.Completed, _exportJobRecord.Status);
        }

        [Fact]
        public async Task GivenTaskCouldNotBeAcquired_WhenExecuted_ThenNoFurtherActionShouldBeTaken()
        {
            _fhirOperationDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, _cancellationToken).Returns(_ => Task.Run(new Func<Task<ExportJobOutcome>>(() => throw new JobConflictException())));

            await _exportJobTask.ExecuteAsync(_cancellationToken);

            await _fhirOperationDataStore.ReceivedWithAnyArgs(1).UpdateExportJobAsync(null, null, _cancellationToken);
        }
    }
}
