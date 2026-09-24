// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexHandlerTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();

        [Fact]
        public async Task GivenAGetRequest_WhenGettingAnExistingJob_ThenHttpResponseCodeShouldBeOk()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = CreateJobRecord();
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            var result = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        [Fact]
        public async Task GivenAGetRequest_WhenUserUnauthorized_ThenUnauthorizedFhirExceptionThrown()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = CreateJobRecord();
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, authorizationService);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => handler.HandleAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAGetRequest_WhenIdNotFound_ThenJobNotFoundExceptionThrown()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = CreateJobRecord();
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Throws(new JobNotFoundException("not found"));

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            await Assert.ThrowsAsync<JobNotFoundException>(() => handler.HandleAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAGetRequest_WhenTooManyRequestsThrown_ThenTooManyRequestsThrown()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = CreateJobRecord();
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", CancellationToken.None).Throws(new Exception(null, new RequestRateExceededException(TimeSpan.FromMilliseconds(100))));

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            Exception thrownException = await Assert.ThrowsAsync<Exception>(() => handler.HandleAsync(request, CancellationToken.None));
            Assert.IsType<RequestRateExceededException>(thrownException.InnerException);
        }

        [Fact]
        public async Task GivenACancelRequest_WhenUserUnauthorized_ThenUnauthorizedFhirExceptionThrown()
        {
            var request = new CancelReindexRequest("id");

            var jobRecord = CreateJobRecord();
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var handler = new CancelReindexRequestHandler(_fhirOperationDataStore, authorizationService);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => handler.HandleAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenACancelRequest_WhenJobCompleted_ThenRequestNotValidExceptionThrown()
        {
            var request = new CancelReindexRequest("id");

            var jobRecord = CreateJobRecord(OperationStatus.Completed);

            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var handler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            await Assert.ThrowsAsync<RequestNotValidException>(() => handler.HandleAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenACancelRequest_WhenJobInProgress_ThenJobUpdatedToCanceled()
        {
            var request = new CancelReindexRequest("id");

            var jobRecord = CreateJobRecord(OperationStatus.Running);
            var canceledJobRecord = CreateJobRecord(OperationStatus.Canceled);

            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            var canceledJobWrapper = new ReindexJobWrapper(canceledJobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);
            _fhirOperationDataStore.CancelReindexJobAsync("id", Arg.Any<CancellationToken>()).Returns(canceledJobWrapper);

            var handler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            var result = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Equal(OperationStatus.Canceled, result.Job.JobRecord.Status);
        }

        [Fact]
        public async Task GivenNoActiveReindexJob_WhenCreatingReindexRequest_ThenNewJobIsCreated()
        {
            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            var createdWrapper = new ReindexJobWrapper(CreateJobRecord(OperationStatus.Queued), WeakETag.FromVersionId("1"));

            _fhirOperationDataStore.CheckActiveReindexJobsAsync(Arg.Any<CancellationToken>())
                .Returns((false, null));
            _fhirOperationDataStore.CreateReindexJobAsync(Arg.Any<ReindexJobRecord>(), Arg.Any<CancellationToken>())
                .Returns(createdWrapper);

            var handler = new CreateReindexRequestHandler(
                _fhirOperationDataStore,
                DisabledFhirAuthorizationService.Instance,
                Options.Create(new ReindexJobConfiguration()));

            var response = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Same(createdWrapper, response.Job);
            await _fhirOperationDataStore.Received(1).CreateReindexJobAsync(Arg.Any<ReindexJobRecord>(), Arg.Any<CancellationToken>());
            await _fhirOperationDataStore.DidNotReceive().GetReindexJobByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenActiveReindexJob_WhenCreatingReindexRequest_ThenExistingJobIsReturned()
        {
            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            var existingWrapper = new ReindexJobWrapper(CreateJobRecord(OperationStatus.Running), WeakETag.FromVersionId("1"));

            _fhirOperationDataStore.CheckActiveReindexJobsAsync(Arg.Any<CancellationToken>())
                .Returns((true, "123"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("123", Arg.Any<CancellationToken>())
                .Returns(existingWrapper);

            var handler = new CreateReindexRequestHandler(
                _fhirOperationDataStore,
                DisabledFhirAuthorizationService.Instance,
                Options.Create(new ReindexJobConfiguration()));

            var response = await handler.HandleAsync(request, CancellationToken.None);

            Assert.Same(existingWrapper, response.Job);
            await _fhirOperationDataStore.Received(1).GetReindexJobByIdAsync("123", Arg.Any<CancellationToken>());
            await _fhirOperationDataStore.DidNotReceive().CreateReindexJobAsync(Arg.Any<ReindexJobRecord>(), Arg.Any<CancellationToken>());
        }

        private ReindexJobRecord CreateJobRecord(OperationStatus status = OperationStatus.Queued)
        {
            return new ReindexJobRecord(1)
            {
                Status = status,
            };
        }
    }
}
