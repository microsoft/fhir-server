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
        /// When the user is not authorized, an UnauthorizedFhirActionException should be thrown.
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
        /// By Orchestrator job Id:
        ///   If Orchestrator job is in Cancelled status or cancel is requested, GetExportJobByIdAsync throws 404.
        ///   If Orchestrator job is in CancelledByUser status, GetExportJobByIdAsync throws 404.
        /// By Processing job Id:
        ///   If Orchestrator job is in Cancelled status or cancel is requested, GetExportJobByIdAsync throws 404.
        ///   If Orchestrator job is in CancelledByUser status, GetExportJobByIdAsync throws 404.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsJobNotFoundException_ThenJobNotFoundExceptionShouldBeThrown()
        {
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new JobNotFoundException(string.Format(Core.Resources.JobNotFound, JobId))));

            await Assert.ThrowsAsync<JobNotFoundException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        /// <summary>
        /// By Orchestrator job Id:
        ///   If Orchestrator job is in Completed status:
        ///     Processing jobs are running > return Running > Set Orchestrator job status to Cancel here (SP will set it to CancelByUser)
        ///     Processing jobs are cancelled and no failed jobs exists > return Cancelled > Set Orchestrator job status to Cancel here (SP will set it to CancelByUser)
        ///     Processing jobs are cancelled and failed jobs exists > return Failed > Set Orchestrator job status to Cancel here (SP will set it to CancelByUser)
        /// By Processing job Id:
        ///   If Processing job is in Completed status:
        ///     Processing jobs are running > return Running > Set Orchestrator job status to Cancel here (SP will set it to CancelByUser)
        ///     Processing jobs are cancelled and no failed jobs exists > return Cancelled > Set Orchestrator job status to Cancel here (SP will set it to CancelByUser)
        ///     Processing jobs are cancelled and failed jobs exists > return Failed > Set Orchestrator job status to Cancel here (SP will set it to CancelByUser)
        ///   If Processing job is in Failed status > return Failed > Set to Cancel here (SP will set the Orchestrator status to CancelByUser, processing job status stays Failed)
        ///   If Processing job is in Cancelled status > return Cancelled > Set to Cancel here (SP will set the Orchestrator status to CancelByUser, processing job status stays Cancelled)
        ///
        /// GetExportJobByIdAsync returns Running/Cancelled/Failed based on group job analysis.
        /// It return 404 if job's status is cancelled or cancelledByUser or CancelRequested = 1
        /// The handler always sets status to Canceled, CanceledTime, and FailureDetails, then calls UpdateExportJobAsync.
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

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(outcome.JobRecord, outcome.ETag, _cancellationToken);
        }

        /// <summary>
        /// When a JobConflictException is encountered, the handler retries up to the configured retry count.
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
                _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), weakETags[index], Arg.Any<CancellationToken>())
                    .Returns(returnThis);
            }
        }

        /// <summary>
        /// When the retry count is exceeded, the JobConflictException is thrown to the caller.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingExportJobEncountersJobConflictExceptionExceedsMaxRetry_ThenExceptionShouldBeThrown()
        {
            _retryCount = 3;

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(_ => CreateExportJobOutcome(CreateExportJobRecord(OperationStatus.Queued)));

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(_ => throw new JobConflictException());

            await Assert.ThrowsAsync<JobConflictException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));
        }

        /// <summary>
        /// When UpdateExportJobAsync throws an unexpected exception (not JobConflictException),
        /// the handler should not retry and should propagate the exception directly.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenUpdateExportJobThrowsUnexpectedException_ThenExceptionShouldBeThrown()
        {
            SetupExportJob(OperationStatus.Queued);

            _fhirOperationDataStore.UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>())
                .Returns<ExportJobOutcome>(_ => throw new InvalidOperationException("Unexpected error"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => _mediator.CancelExportAsync(JobId, _cancellationToken));

            await _fhirOperationDataStore.Received(1).UpdateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>());
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
