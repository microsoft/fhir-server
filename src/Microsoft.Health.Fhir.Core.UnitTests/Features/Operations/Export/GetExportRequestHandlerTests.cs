// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Health.Fhir.Core.Models;
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
        /// Verifies that an <see cref="UnauthorizedFhirActionException"/> is thrown when the user
        /// does not have the <see cref="DataActions.Export"/> permission. The handler checks
        /// authorization before any data store interaction.
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
        /// Verifies that a <see cref="JobNotFoundException"/> propagates when
        /// <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> throws it.
        /// This occurs when the job Id does not exist, the job group contains a CancelledByUser
        /// processing job (i.e., cancel was already requested by the user), or the job is in
        /// Cancelled/CancelRequested state in the queue.
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
        /// Verifies that when the job status is Completed or Canceled, the handler returns
        /// <see cref="HttpStatusCode.OK"/> with a populated <see cref="ExportJobResult"/>.
        ///
        /// Both statuses are handled identically by the first branch in the handler:
        ///   - Completed: all processing jobs finished successfully; output contains the full result set.
        ///   - Canceled: a user-initiated cancel was processed but GetExportJobByIdAsync still returned
        ///     the job (no CancelledByUser processing job yet). The handler returns OK with whatever
        ///     partial output was produced before cancellation.
        ///
        /// The output count should match the number of resource types exported.
        /// Covers: no resource types (empty output), single resource type, and multiple resource types.
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
        /// Verifies that when the job status is Failed, an <see cref="OperationFailedException"/> is thrown.
        ///
        /// The data store may return Failed status in several scenarios:
        ///   - The orchestrator job itself failed.
        ///   - The orchestrator completed but one or more processing jobs failed. In this case,
        ///     <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> aggregates the child failure
        ///     details into the record and returns Failed status.
        ///
        /// If FailureDetails is present, the failure reason and status code come from it.
        /// If FailureDetails is null, a default error message and <see cref="HttpStatusCode.InternalServerError"/>
        /// status code are used.
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
        /// Verifies that when the job status is Running or Queued, the handler returns
        /// <see cref="HttpStatusCode.Accepted"/> with a null JobResult.
        ///
        /// These statuses indicate the export is still in progress:
        ///   - Queued: the job has been created but not yet picked up by a worker.
        ///   - Running: the orchestrator or processing jobs are actively executing.
        ///     This also covers the scenario where the orchestrator completed but processing
        ///     jobs are still in-flight — <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/>
        ///     returns Running status in that case.
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
        /// Verifies that when the job status is Completed and the job has error files,
        /// the error output is included in the response alongside the successful output.
        /// Per the FHIR Bulk Data IG, the response body includes both "output" (successful files)
        /// and "error" (OperationOutcome ndjson files) arrays.
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
        /// Verifies that when <see cref="IFhirOperationDataStore.GetExportJobByIdAsync"/> throws an
        /// unexpected exception (not <see cref="JobNotFoundException"/>), it propagates to the caller
        /// without being caught or transformed by the handler.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsUnexpectedException_ThenExceptionShouldBeThrown()
        {
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new InvalidOperationException("Unexpected error")));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
        }

        /// <summary>
        /// Verifies that when the job is Completed with issues (OperationOutcomeIssue entries),
        /// the issues are included in the response. Issues represent non-fatal warnings or
        /// informational messages encountered during export processing.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithIssues_ThenIssuesShouldBeIncludedInResponse()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            jobRecord.Output.Add("Patient", new List<ExportFileInfo>
            {
                new ExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), sequence: 1),
            });
            jobRecord.Issues.Add(new OperationOutcomeIssue("warning", "informational", diagnostics: "Some resources were skipped"));

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.NotNull(response.JobResult.Issues);
            Assert.Single(response.JobResult.Issues);
            Assert.Equal("warning", response.JobResult.Issues[0].Severity);
        }

        /// <summary>
        /// Verifies that the handler sorts output entries alphabetically by resource type.
        /// The FHIR Bulk Data IG does not mandate ordering, but the handler applies
        /// <see cref="StringComparer.Ordinal"/> sorting for deterministic responses.
        /// This test uses resource types added in reverse alphabetical order to confirm sorting.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithMultipleResourceTypes_ThenOutputShouldBeSortedByType()
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
            jobRecord.Output.Add("Condition", new List<ExportFileInfo>
            {
                new ExportFileInfo("Condition", new Uri("http://example.com/condition.ndjson"), sequence: 1),
            });

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, response.JobResult.Output.Count);

            List<string> outputTypes = response.JobResult.Output.Select(o => o.Type).ToList();
            Assert.Equal(new[] { "Condition", "Observation", "Patient" }, outputTypes);
        }

        /// <summary>
        /// Verifies that when a Canceled job has partial output and error files, the handler
        /// returns <see cref="HttpStatusCode.OK"/> with whatever results were produced before
        /// cancellation. This covers the scenario where the user cancelled the export but some
        /// processing jobs had already completed and written output files.
        /// </summary>
        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCanceledJobWithPartialResults_ThenOkResponseShouldIncludePartialOutputAndErrors()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Canceled);
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
