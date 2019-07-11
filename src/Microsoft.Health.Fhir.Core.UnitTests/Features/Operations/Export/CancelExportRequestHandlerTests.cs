// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class CancelExportRequestHandlerTests
    {
        private const string JobId = "jobId";

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IMediator _mediator;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        private int _retryCount = 0;
        private Func<int, TimeSpan> _sleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(0));

        public CancelExportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(sp => new CancelExportRequestHandler(_fhirOperationDataStore, _retryCount, _sleepDurationProvider)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Theory]
        [InlineData(OperationStatus.Cancelled)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobHasAlreadyCompleted_ThenConflictStatusCodeShouldbeReturned(OperationStatus operationStatus)
        {
            ExportJobOutcome outcome = await ExecuteCancelExportAsync(operationStatus, HttpStatusCode.Conflict);

            Assert.Equal(operationStatus, outcome.JobRecord.Status);
            Assert.Null(outcome.JobRecord.CancelledTime);
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobThatHasNotCompleted_ThenConflictStatusCodeShouldbeReturned(OperationStatus operationStatus)
        {
            ExportJobOutcome outcome = null;

            var instant = new DateTimeOffset(2019, 5, 3, 22, 45, 15, TimeSpan.FromMinutes(-60));

            using (Mock.Property(() => Clock.UtcNowFunc, () => instant))
            {
                outcome = await ExecuteCancelExportAsync(operationStatus, HttpStatusCode.Accepted);
            }

            // Check to make sure the record is updated
            Assert.Equal(OperationStatus.Cancelled, outcome.JobRecord.Status);
            Assert.Equal(instant, outcome.JobRecord.CancelledTime);

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, _cancellationToken);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobEncountersJobConflictException_ThenItWillBeRetried()
        {
            _retryCount = 3;

            ExportJobOutcome outcome = SetupExportJob(OperationStatus.Queued);

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>())
                .Returns(x => throw new JobConflictException(), x => throw new JobConflictException(), x => outcome);

            // No error should be thrown.
            await _mediator.CancelExportAsync(JobId, _cancellationToken);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobEncountersJobConflictExceptionExceedsMaxRetry_ThenExceptionShouldBeThrown()
        {
            _retryCount = 3;

            ExportJobOutcome outcome = SetupExportJob(OperationStatus.Queued);

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(x => throw new JobConflictException());

            // Error should be thrown.
            await Assert.ThrowsAsync<JobConflictException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        private async Task<ExportJobOutcome> ExecuteCancelExportAsync(OperationStatus operationStatus, HttpStatusCode expectedStatusCode)
        {
            ExportJobOutcome outcome = SetupExportJob(operationStatus);

            CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(expectedStatusCode, response.StatusCode);

            return outcome;
        }

        private ExportJobOutcome SetupExportJob(OperationStatus operationStatus)
        {
            var outcome = new ExportJobOutcome(
                new ExportJobRecord(new Uri("http://localhost/job/"), "Patient", "123", null)
                {
                    Status = operationStatus,
                },
                WeakETag.FromVersionId("123"));

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            return outcome;
        }
    }
}
