// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Security.Authorization;
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
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Index)]
    public class ReindexHandlerTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private IReadOnlyDictionary<string, string> _resourceTypeSearchParameterHashMap;

        public ReindexHandlerTests()
        {
            _resourceTypeSearchParameterHashMap = new Dictionary<string, string>() { { "resourceType", "paramHash" } };
        }

        [Fact]
        public async Task GivenAGetRequest_WhenGettingAnExistingJob_ThenHttpResponseCodeShouldBeOk()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1);
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            var result = await handler.Handle(request, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        [Fact]
        public async Task GivenAGetRequest_WhenUserUnauthorized_ThenUnauthorizedFhirExceptionThrown()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1);
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, authorizationService);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAGetRequest_WhenIdNotFound_ThenJobNotFoundExceptionThrown()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1);
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Throws(new JobNotFoundException("not found"));

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            await Assert.ThrowsAsync<JobNotFoundException>(() => handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAGetRequest_WhenTooManyRequestsThrown_ThenTooManyRequestsThrown()
        {
            var request = new GetReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1);
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", CancellationToken.None).Throws(new Exception(null, new RequestRateExceededException(TimeSpan.FromMilliseconds(100))));

            var handler = new GetReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            Exception thrownException = await Assert.ThrowsAsync<Exception>(() => handler.Handle(request, CancellationToken.None));
            Assert.IsType<RequestRateExceededException>(thrownException.InnerException);
        }

        [Fact]
        public async Task GivenACancelRequest_WhenUserUnauthorized_ThenUnauthorizedFhirExceptionThrown()
        {
            var request = new CancelReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1);
            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            authorizationService.CheckAccess(DataActions.Reindex, Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var handler = new CancelReindexRequestHandler(_fhirOperationDataStore, authorizationService);

            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenACancelRequest_WhenJobCompleted_ThenRequestNotValidExceptionThrown()
        {
            var request = new CancelReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1)
            {
                Status = OperationStatus.Completed,
            };

            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var handler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            await Assert.ThrowsAsync<RequestNotValidException>(() => handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenACancelRequest_WhenJobInProgress_ThenJobUpdatedToCanceled()
        {
            var request = new CancelReindexRequest("id");

            var jobRecord = new ReindexJobRecord(_resourceTypeSearchParameterHashMap, new List<string>(), 1)
            {
                Status = OperationStatus.Running,
            };

            var jobWrapper = new ReindexJobWrapper(jobRecord, WeakETag.FromVersionId("id"));
            _fhirOperationDataStore.GetReindexJobByIdAsync("id", Arg.Any<CancellationToken>()).Returns(jobWrapper);
            _fhirOperationDataStore.UpdateReindexJobAsync(jobRecord, WeakETag.FromVersionId("id"), Arg.Any<CancellationToken>()).Returns(jobWrapper);

            var handler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);

            var result = await handler.Handle(request, CancellationToken.None);

            Assert.Equal(OperationStatus.Canceled, result.Job.JobRecord.Status);
        }
    }
}
