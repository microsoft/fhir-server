// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
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
    public class GetExportRequestHandlerTests
    {
        private const string JobId = "jobId";

        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IMediator _mediator;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        public GetExportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection
                .Add(sp => new GetExportRequestHandler(
                    _fhirOperationDataStore,
                    DisabledFhirAuthorizationService.Instance))
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

            var handler = new GetExportRequestHandler(
                _fhirOperationDataStore,
                authorizationService);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() =>
                handler.Handle(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
        }

        /// <summary>
        /// By Orchestrator or Processing job Id:
        ///   If Orchestrator job is in Cancelled status, cancel is requested, or CancelledByUser status,
        ///   GetExportJobByIdAsync throws JobNotFoundException (404).
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsJobNotFoundException_ThenJobNotFoundExceptionShouldBeThrown()
        {
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new JobNotFoundException(string.Format(Core.Resources.JobNotFound, JobId))));

            await Assert.ThrowsAsync<JobNotFoundException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
        }

        /// <summary>
        /// When the job status is Completed, the handler returns OK with the job result containing output files.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJob_ThenOkWithResultShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            var fileInfo = new ExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), sequence: 1);
            jobRecord.Output.Add("Patient", new List<ExportFileInfo> { fileInfo });

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.Single(response.JobResult.Output);

            await _fhirOperationDataStore.Received(1).GetExportJobByIdAsync(JobId, _cancellationToken);
        }

        /// <summary>
        /// When the job status is Completed and there are multiple resource types in the output,
        /// all outputs should be returned.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithMultipleResourceTypes_ThenAllOutputsShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            jobRecord.Output.Add("Patient", new List<ExportFileInfo>
            {
                new ExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), sequence: 1),
            });
            jobRecord.Output.Add("Observation", new List<ExportFileInfo>
            {
                new ExportFileInfo("Observation", new Uri("http://example.com/observation.ndjson"), sequence: 1),
            });

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.Equal(2, response.JobResult.Output.Count);
        }

        /// <summary>
        /// The handler treats Canceled the same as Completed (returns OK with output).
        /// This covers the scenario where a cancel was requested but partial results are available.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCanceledJob_ThenOkWithResultShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Canceled);
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        /// <summary>
        /// When the job status is Failed, an OperationFailedException should be thrown with the failure details.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingFailedJob_ThenOperationFailedExceptionShouldBeThrown()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Failed);
            jobRecord.FailureDetails = new JobFailureDetails("Export job failed", HttpStatusCode.InternalServerError);

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            var ex = await Assert.ThrowsAsync<OperationFailedException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
            Assert.Equal(HttpStatusCode.InternalServerError, ex.ResponseStatusCode);
        }

        /// <summary>
        /// When the job status is Failed but FailureDetails is null, the handler should still throw
        /// an OperationFailedException with a default error message and InternalServerError status code.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingFailedJobWithoutFailureDetails_ThenOperationFailedExceptionWithDefaultMessageShouldBeThrown()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Failed);

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            var ex = await Assert.ThrowsAsync<OperationFailedException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
            Assert.Equal(HttpStatusCode.InternalServerError, ex.ResponseStatusCode);
        }

        /// <summary>
        /// When the job status is Running, the handler returns Accepted indicating the job is still in progress.
        /// By Processing job Id:
        ///   If Processing job is in Running status > return Running from GetExportJobByIdAsync > handler returns Accepted.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingRunningJob_ThenAcceptedShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Running);
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await _fhirOperationDataStore.Received(1).GetExportJobByIdAsync(JobId, _cancellationToken);
        }

        /// <summary>
        /// When the job status is Queued (maps to Created at the queue level), the handler returns Accepted.
        /// By Processing job Id:
        ///   If Processing job is in Created status > return Created from GetExportJobByIdAsync > handler returns Accepted.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingQueuedJob_ThenAcceptedShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Queued);
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        /// <summary>
        /// When the Completed status is returned from GetExportJobByIdAsync because processing jobs are still running
        /// (in-flight), the data store returns Running status. The handler then returns Accepted.
        /// By Orchestrator job Id:
        ///   If Orchestrator job is in Completed status and processing jobs are running > return Running > handler returns Accepted.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingJobWithRunningProcessingJobs_ThenAcceptedShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Running);
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        /// <summary>
        /// By Orchestrator job Id:
        ///   If Orchestrator job is in Completed status and processing jobs are cancelled with failed jobs,
        ///   GetExportJobByIdAsync returns Failed status. The handler throws OperationFailedException.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingJobWithFailedProcessingJobs_ThenOperationFailedExceptionShouldBeThrown()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Failed);
            jobRecord.FailureDetails = new JobFailureDetails("Processing job failed", HttpStatusCode.InternalServerError);

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            var ex = await Assert.ThrowsAsync<OperationFailedException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
            Assert.Equal(HttpStatusCode.InternalServerError, ex.ResponseStatusCode);
        }

        /// <summary>
        /// When the job status is Completed with empty output, the handler returns OK with an empty output list.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithEmptyOutput_ThenOkWithEmptyOutputShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.Empty(response.JobResult.Output);
        }

        /// <summary>
        /// When the job status is Completed with multiple resource types, the output should be sorted
        /// alphabetically by resource type using ordinal comparison.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithMultipleOutputTypes_ThenOutputShouldBeSortedByType()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            jobRecord.Output.Add("Patient", new List<ExportFileInfo>
            {
                new ExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), sequence: 1),
            });
            jobRecord.Output.Add("Condition", new List<ExportFileInfo>
            {
                new ExportFileInfo("Condition", new Uri("http://example.com/condition.ndjson"), sequence: 1),
            });
            jobRecord.Output.Add("Observation", new List<ExportFileInfo>
            {
                new ExportFileInfo("Observation", new Uri("http://example.com/observation.ndjson"), sequence: 1),
            });

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, response.JobResult.Output.Count);
            Assert.Equal("Condition", response.JobResult.Output[0].Type);
            Assert.Equal("Observation", response.JobResult.Output[1].Type);
            Assert.Equal("Patient", response.JobResult.Output[2].Type);
        }

        /// <summary>
        /// When the job status is Running or Queued (Accepted), the response should have a null JobResult.
        /// </summary>
        [Theory]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingInProgressJob_ThenAcceptedWithNullJobResultShouldBeReturned(OperationStatus status)
        {
            var jobRecord = CreateExportJobRecord(status);
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Null(response.JobResult);
        }

        /// <summary>
        /// When the job status is Completed and the job has error files, the error output
        /// should be included in the response.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithErrors_ThenErrorOutputShouldBeIncluded()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            jobRecord.Output.Add("Patient", new List<ExportFileInfo>
            {
                new ExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), sequence: 1),
            });
            jobRecord.Error.Add(new ExportFileInfo("OperationOutcome", new Uri("http://example.com/error.ndjson"), sequence: 1));

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.Single(response.JobResult.Output);
            Assert.Single(response.JobResult.Error);
            Assert.Equal("OperationOutcome", response.JobResult.Error[0].Type);
        }

        /// <summary>
        /// When GetExportJobByIdAsync throws an unexpected exception (not JobNotFoundException),
        /// it should propagate to the caller.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsUnexpectedException_ThenExceptionShouldBeThrown()
        {
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new InvalidOperationException("Unexpected error")));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
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
