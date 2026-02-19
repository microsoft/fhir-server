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

        [Fact]
        public async Task GivenAFhirMediator_WhenGetExportJobByIdThrowsJobNotFoundException_ThenJobNotFoundExceptionShouldBeThrown()
        {
            // Data store now throws JobNotFoundException for cancelled/user-cancelled jobs
            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken)
                .Returns(Task.FromException<ExportJobOutcome>(new JobNotFoundException(string.Format(Core.Resources.JobNotFound, JobId))));

            await Assert.ThrowsAsync<JobNotFoundException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJob_ThenOkWithResultShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            var fileInfo = CreateExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), 1024);
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

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingFailedJobWithoutFailureDetails_ThenOperationFailedExceptionWithDefaultMessageShouldBeThrown()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Failed);

            // FailureDetails is null
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            var ex = await Assert.ThrowsAsync<OperationFailedException>(() =>
                _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken));
            Assert.Equal(HttpStatusCode.InternalServerError, ex.ResponseStatusCode);
        }

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

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCompletedJobWithMultipleResourceTypes_ThenAllOutputsShouldBeReturned()
        {
            var jobRecord = CreateExportJobRecord(OperationStatus.Completed);
            jobRecord.Output.Add("Patient", new List<ExportFileInfo>
            {
                CreateExportFileInfo("Patient", new Uri("http://example.com/patient.ndjson"), 1024),
            });
            jobRecord.Output.Add("Observation", new List<ExportFileInfo>
            {
                CreateExportFileInfo("Observation", new Uri("http://example.com/observation.ndjson"), 2048),
            });

            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(response.JobResult);
            Assert.Equal(2, response.JobResult.Output.Count);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingCanceledJob_ThenOkWithResultShouldBeReturned()
        {
            // The handler treats Canceled the same as Completed (returns OK with output)
            var jobRecord = CreateExportJobRecord(OperationStatus.Canceled);
            var outcome = CreateExportJobOutcome(jobRecord);

            _fhirOperationDataStore.GetExportJobByIdAsync(JobId, _cancellationToken).Returns(outcome);

            GetExportResponse response = await _mediator.Send(new GetExportRequest(new Uri("http://localhost"), JobId), _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

        private ExportFileInfo CreateExportFileInfo(string type, Uri fileUri, long committedBytes)
        {
            ExportFileInfo fileInfo = (ExportFileInfo)Activator.CreateInstance(typeof(ExportFileInfo), nonPublic: true);
            typeof(ExportFileInfo).GetProperty("Type").SetValue(fileInfo, type);
            typeof(ExportFileInfo).GetProperty("FileUri").SetValue(fileInfo, fileUri);
            typeof(ExportFileInfo).GetProperty("CommittedBytes").SetValue(fileInfo, committedBytes);
            return fileInfo;
        }
    }
}
