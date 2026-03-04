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

        /// <summary>
        /// Verifies that an <see cref="UnauthorizedFhirActionException"/> is thrown when the user
        /// does not have the <see cref="DataActions.Export"/> permission. The handler checks
        /// authorization before performing any cancel work.
        /// </summary>
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

        /// <summary>
        /// Verifies that a <see cref="JobNotFoundException"/> propagates when
        /// <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> throws it.
        /// This occurs when a CancelledByUser processing job already exists in the group
        /// (i.e., cancel was already requested), or when the job Id does not exist.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsJobNotFoundException_ThenJobNotFoundExceptionShouldBeThrown()
        {
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new JobNotFoundException(string.Format(Core.Resources.JobNotFound, JobId))));

            await Assert.ThrowsAsync<JobNotFoundException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        /// <summary>
        /// Verifies the handler's cancel behavior for any status returned by GetExportJobByIdAsync
        /// (i.e., any status that does not result in a 404/JobNotFoundException).
        ///
        /// For each status the handler:
        ///   1. Sets the job record's Status to <see cref="OperationStatus.Canceled"/>.
        ///   2. Sets CanceledTime to the current UTC time.
        ///   3. Sets FailureDetails with <see cref="HttpStatusCode.NoContent"/> and a user-requested cancellation message.
        ///   4. Calls <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/> with isCustomerRequested = true,
        ///      which causes the stored procedure to cancel the job group and enqueue a CancelledByUser processing job
        ///      per the FHIR Bulk Data IG (https://hl7.org/fhir/uv/bulkdata/STU2/export.html#bulk-data-delete-request).
        ///   5. Returns <see cref="HttpStatusCode.Accepted"/>.
        ///
        /// Note: The in-memory Status/FailureDetails set here are serialized into the CancelledByUser job definition
        /// by the data store. The actual group-level cancellation (setting Created jobs to Cancelled,
        /// Running jobs to CancelRequested) is handled by the stored procedure via CancelJobByGroupIdAsync.
        /// </summary>
        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Failed)]
        [InlineData(OperationStatus.Canceled)]
        public async Task GivenAFhirMediator_WhenCancelingJobInAnyNon404Status_ThenRecordIsSetToCanceledWithFailureDetailsAndAcceptedReturned(OperationStatus operationStatus)
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

            Assert.Equal(OperationStatus.Canceled, outcome.JobRecord.Status);
            Assert.NotNull(outcome.JobRecord.CanceledTime);
            Assert.NotNull(outcome.JobRecord.FailureDetails);
            Assert.Equal(HttpStatusCode.NoContent, outcome.JobRecord.FailureDetails.FailureStatusCode);
            Assert.Equal(Core.Resources.UserRequestedCancellation, outcome.JobRecord.FailureDetails.FailureReason);

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, true, _cancellationToken);
        }

        /// <summary>
        /// Verifies that the handler retries when <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/>
        /// throws a <see cref="JobConflictException"/> due to an optimistic concurrency conflict (ETag mismatch).
        /// On each retry the handler re-fetches the job to get a fresh ETag, then attempts the update again.
        /// After the configured number of retries succeeds, the response is <see cref="HttpStatusCode.Accepted"/>.
        /// </summary>
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

            CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            void SetupOperationDataStore(int index, Func<NSubstitute.Core.CallInfo, ExportJobOutcome> returnThis)
            {
                _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), weakETags[index], Arg.Any<bool>(), Arg.Any<CancellationToken>())
                    .Returns(returnThis);
            }
        }

        /// <summary>
        /// Verifies that when <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/> throws
        /// <see cref="JobConflictException"/> on every attempt and the configured retry count is
        /// exceeded, the exception propagates to the caller rather than being silently swallowed.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobEncountersJobConflictExceptionExceedsMaxRetry_ThenExceptionShouldBeThrown()
        {
            _retryCount = 3;

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(_ => CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Queued)));

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(_ => throw new JobConflictException());

            await Assert.ThrowsAsync<JobConflictException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        /// <summary>
        /// Verifies that when <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/> throws an
        /// unexpected exception (anything other than <see cref="JobConflictException"/>), the handler
        /// does not retry and propagates the exception immediately. Only JobConflictException triggers
        /// the retry policy.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenUpdateExportJobThrowsUnexpectedException_ThenExceptionShouldBeThrown()
        {
            SetupExportJob(OperationStatus.Queued);

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(_ => throw new InvalidOperationException("Unexpected error"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// Explicitly verifies that the handler passes isCustomerRequested = true to
        /// <see cref="IFhirOperationDataStore.UpdateExportJobAsync"/>. This flag is critical because
        /// it causes the data store to enqueue a CancelledByUser processing job, which is required by
        /// the FHIR Bulk Data IG for user-initiated export cancellations. Subsequent calls to
        /// GetExportJobByIdAsync will detect this CancelledByUser job and throw JobNotFoundException (404).
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExportJob_ThenIsCustomerRequestedIsSetToTrue()
        {
            SetupExportJob(OperationStatus.Running);

            CancelExportResponse response = await _mediator.CancelExportAsync(JobId, _cancellationToken);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(
                Arg.Any<ExportJobRecord>(),
                Arg.Any<WeakETag>(),
                Arg.Is<bool>(val => val),
                Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// Verifies that when <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> throws an
        /// unexpected exception (not <see cref="JobNotFoundException"/>), the handler does not retry
        /// and propagates the exception immediately. The retry policy only handles
        /// <see cref="JobConflictException"/> from UpdateExportJobAsync.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsUnexpectedException_ThenExceptionShouldBeThrownWithoutRetry()
        {
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns<ExportJobOutcome>(_ => throw new InvalidOperationException("Unexpected data store error"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));

            await _fhirOperationDataStore.Received(1).GetExportJobByIdAsync(JobId, _cancellationToken);
            await _fhirOperationDataStore.DidNotReceive().UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// Verifies the scenario where the first cancel attempt hits a <see cref="JobConflictException"/>
        /// (triggering a retry), but by the time the handler re-fetches the job on the retry,
        /// the job has already been cancelled (a CancelledByUser processing job now exists in the group),
        /// causing <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> to throw
        /// <see cref="JobNotFoundException"/>. The handler should propagate the JobNotFoundException
        /// since the cancel was already applied.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenRetryEncountersJobNotFoundOnRefetch_ThenJobNotFoundExceptionShouldBeThrown()
        {
            _retryCount = 3;

            var weakETag = WeakETag.FromVersionId("1");
            int getCallCount = 0;

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(_ =>
                {
                    getCallCount++;
                    if (getCallCount == 1)
                    {
                        return CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Running), weakETag);
                    }

                    throw new JobNotFoundException(string.Format(Core.Resources.JobNotFound, JobId));
                });

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), weakETag, Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(_ => throw new JobConflictException());

            await Assert.ThrowsAsync<JobNotFoundException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
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
