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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
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
            collection
                .Add(sp => new CancelExportRequestHandler(
                    _fhirOperationDataStore,
                    DisabledFhirAuthorizationService.Instance,
                    _retryCount,
                    _sleepDurationProvider,
                    NullLogger<CancelExportRequestHandler>.Instance))
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(provider);
        }

        [Theory]
        [InlineData(OperationStatus.Canceled)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobThatHasAlreadyCompleted_ThenConflictStatusCodeShouldBeReturned(OperationStatus operationStatus)
        {
            ExportJobOutcome outcome = await SetupAndExecuteCancelExportAsync(operationStatus, HttpStatusCode.Conflict);

            Assert.Equal(operationStatus, outcome.JobRecord.Status);
            Assert.Null(outcome.JobRecord.CanceledTime);
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobThatHasNotCompleted_ThenAcceptedStatusCodeShouldBeReturned(OperationStatus operationStatus)
        {
            ExportJobOutcome outcome = null;

            var instant = new DateTimeOffset(2019, 5, 3, 22, 45, 15, TimeSpan.FromMinutes(-60));

            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => instant))
            {
                outcome = await SetupAndExecuteCancelExportAsync(operationStatus, HttpStatusCode.Accepted);
            }

            // Check to make sure the record is updated
            Assert.Equal(OperationStatus.Canceled, outcome.JobRecord.Status);
            Assert.Equal(instant, outcome.JobRecord.CanceledTime);

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, _cancellationToken);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobEncountersJobConflictException_ThenItWillBeRetried()
        {
            _retryCount = 3;

            var weakETags = new WeakETag[]
            {
                WeakETag.FromVersionId("1"),
                WeakETag.FromVersionId("2"),
                WeakETag.FromVersionId("3"),
            };

            var jobRecord = CreateExportJobRecord(OperationStatus.Queued);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(
                    _ => CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Queued), weakETags[0]),
                    _ => CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Queued), weakETags[1]),
                    _ => CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Queued), weakETags[2]));

            SetupOperationDataStore(0, _ => throw new JobConflictException());
            SetupOperationDataStore(1, _ => throw new JobConflictException());
            SetupOperationDataStore(2, _ => CreateExportJobOutcome(jobRecord, WeakETag.FromVersionId("123")));

            // No error should be thrown.
            CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            void SetupOperationDataStore(int index, Func<NSubstitute.Core.CallInfo, ExportJobOutcome> returnThis)
            {
                _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), weakETags[index], Arg.Any<CancellationToken>())
                    .Returns(returnThis);
            }
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobEncountersJobConflictExceptionExceedsMaxRetry_ThenExceptionShouldBeThrown()
        {
            _retryCount = 3;

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(_ => CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Queued)));

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(_ => throw new JobConflictException());

            // Error should be thrown.
            await Assert.ThrowsAsync<JobConflictException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        private async Task<ExportJobOutcome> SetupAndExecuteCancelExportAsync(OperationStatus operationStatus, HttpStatusCode expectedStatusCode)
        {
            ExportJobOutcome outcome = SetupExportJob(operationStatus);

            CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(expectedStatusCode, response.StatusCode);

            return outcome;
        }

        private ExportJobOutcome SetupExportJob(OperationStatus operationStatus, WeakETag weakETag = null)
        {
            var outcome = CreateExportJobOutcome(
                CreateExportJobRecord(operationStatus),
                weakETag);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            return outcome;
        }

        private ExportJobRecord CreateExportJobRecord(OperationStatus operationStatus)
        {
            return new ExportJobRecord(
                new Uri("http://localhost/job/"),
                ExportJobType.Patient,
                ExportFormatTags.ResourceName,
                resourceType: null,
                filters: null,
                hash: "123",
                rollingFileSizeInMB: 64,
                requestorClaims: null)
            {
                Status = operationStatus,
            };
        }

        private ExportJobOutcome CreateExportJobOutcome(ExportJobRecord exportJobRecord, WeakETag weakETag = null)
        {
            return new ExportJobOutcome(
                exportJobRecord,
                weakETag ?? WeakETag.FromVersionId("123"));
        }
    }
}
