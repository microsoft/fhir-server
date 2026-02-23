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

        private static readonly string[] ResourceTypes = ["Patient", "Observation"];

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
        /// When the job status is Completed or Canceled, the handler returns OK with the job result.
        /// The output count should match the number of resource types exported.
        /// Covers: no resource types (empty output), single resource type, and multiple resource types.
        /// The handler treats Canceled the same as Completed (returns OK with partial results).
        /// </summary>
        [Theory]
        [InlineData(OperationStatus.Completed, 0)]
        [InlineData(OperationStatus.Completed, 1)]
        [InlineData(OperationStatus.Completed, 2)]
        [InlineData(OperationStatus.Canceled, 0)]
        [InlineData(OperationStatus.Canceled, 1)]
        [InlineData(OperationStatus.Canceled, 2)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedOrCancelledStatus_ThenHttpResponseCodeShouldBeOk(OperationStatus status, int resourceTypeCount)
        {
            var jobRecord = CreateExportJobRecord(status);
            for (int i = 0; i < resourceTypeCount; i++)
            {
                string type = ResourceTypes[i];
                jobRecord.Output.Add(type, new List<ExportFileInfo>
                {
                    new ExportFileInfo(type, new Uri($"http://example.com/{type.ToLowerInvariant()}.ndjson"), sequence: i),
                });
            }

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.Equal(resourceTypeCount, response.JobResult.Output.Count);
            await _fhirOperationDataStore.Received(1).GetExportJobByIdAsync(JobId, _cancellationToken);
        }

        /// <summary>
        /// When the job status is Failed, an OperationFailedException should be thrown.
        /// If FailureDetails is present, the failure reason and status code come from it.
        /// If FailureDetails is null, a default error message and InternalServerError status code are used.
        /// This also covers the orchestrator-completed-with-failed-processing-jobs scenario,
        /// where GetExportJobByIdAsync returns Failed status with the child's failure details.
        /// </summary>
        [Theory]
        [InlineData(null, HttpStatusCode.InternalServerError)]
        [InlineData("Export job failed", HttpStatusCode.InternalServerError)]
        [InlineData("Bad input data", HttpStatusCode.BadRequest)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithFailedStatus_ThenOperationFailedExceptionIsThrownWithCorrectHttpResponseCode(string failureReason, HttpStatusCode expectedStatusCode)
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Failed);
            if (failureReason != null)
            {
                jobRecord.FailureDetails = new JobFailureDetails(failureReason, expectedStatusCode);
            }

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            var ex = await Assert.ThrowsAsync<OperationFailedException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
            Assert.Equal(expectedStatusCode, ex.ResponseStatusCode);

            if (failureReason != null)
            {
                Assert.Contains(failureReason, ex.Message);
            }
            else
            {
                Assert.Contains(Core.Resources.UnknownError, ex.Message);
            }
        }

        /// <summary>
        /// When the job status is Running or Queued, the handler returns Accepted with a null JobResult.
        /// This covers:
        ///   - Processing job in Running status > handler returns Accepted.
        ///   - Processing job in Created/Queued status > handler returns Accepted.
        ///   - Orchestrator completed but processing jobs still in-flight > data store returns Running > handler returns Accepted.
        /// </summary>
        [Theory]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted(OperationStatus status)
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
