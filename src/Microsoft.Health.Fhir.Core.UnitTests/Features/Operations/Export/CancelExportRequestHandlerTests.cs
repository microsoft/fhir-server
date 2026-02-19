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
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
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

        [Fact]
        public async Task GivenAFhirMediator_WhenUserIsNotAuthorized_ThenUnauthorizedFhirActionExceptionShouldBeThrown()
        {
            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Export, Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var handler = new CancelExportRequestHandler(
                _fhirOperationDataStore,
                authorizationService,
                _retryCount,
                _sleepDurationProvider,
                NullLogger<CancelExportRequestHandler>.Instance);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                handler.Handle(new CancelExportRequest(JobId), _cancellationToken));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingAlreadyCanceledJob_ThenOperationFailedExceptionWithConflictShouldBeThrown()
        {
            SetupExportJob(OperationStatus.Canceled);

            var ex = await Assert.ThrowsAsync<OperationFailedException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
            Assert.Equal(HttpStatusCode.Conflict, ex.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsJobNotFoundException_ThenJobNotFoundExceptionShouldBeThrown()
        {
            // Simulate the data store throwing JobNotFoundException (e.g., job was cancelled by user and data store now returns 404)
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new JobNotFoundException(string.Format(Core.Resources.JobNotFound, JobId))));

            await Assert.ThrowsAsync<JobNotFoundException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobThatHasNotCompleted_ThenAcceptedStatusCodeShouldBeReturned(OperationStatus operationStatus)
        {
            ExportJobOutcome outcome = null;

            var instant = new DateTimeOffset(2019, 5, 3, 22, 45, 15, TimeSpan.FromMinutes(-60));

            Microsoft.Extensions.Time.Testing.FakeTimeProvider timeProvider = new(instant);
            using (Mock.Property(() => ClockResolver.TimeProvider, timeProvider))
            {
                outcome = SetupExportJob(operationStatus);

                CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

                Assert.NotNull(response);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            }

            // Check to make sure the record is updated to Canceled
            Assert.Equal(OperationStatus.Canceled, outcome.JobRecord.Status);
            Assert.Equal(instant, outcome.JobRecord.CanceledTime);
            Assert.NotNull(outcome.JobRecord.FailureDetails);
            Assert.Equal(HttpStatusCode.NoContent, outcome.JobRecord.FailureDetails.FailureStatusCode);

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, _cancellationToken);
        }

        [Theory]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        public async Task GivenAFhirMediator_WhenCancelingFinishedJob_ThenStatusShouldNotChangeButCanceledTimeAndFailureDetailsAreSet(OperationStatus operationStatus)
        {
            var outcome = SetupExportJob(operationStatus);

            CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            // Status should NOT change for Completed/Failed - only Queued/Running gets updated to Canceled
            Assert.Equal(operationStatus, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.CanceledTime);
            Assert.NotNull(outcome.JobRecord.FailureDetails);

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
                requestorClaims: null,
                groupId: null)
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
