// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkDelete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class CreateBulkDeleteHandlerTests
    {
        private IAuthorizationService<DataActions> _authorizationService;
        private IQueueClient _queueClient;
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private ISearchService _searchService;
        private CreateBulkDeleteHandler _handler;
        private string _testUrl = "https://test.com/";

        public CreateBulkDeleteHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _queueClient = Substitute.For<IQueueClient>();
            _searchService = Substitute.For<ISearchService>();

            _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _contextAccessor.RequestContext = new FhirRequestContext(
                method: "bulkdeletetest",
                uriString: _testUrl,
                baseUriString: _testUrl,
                correlationId: "bulkdeletetest",
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: new Dictionary<string, StringValues>());

            _handler = new CreateBulkDeleteHandler(_authorizationService, _queueClient, _contextAccessor, _searchService);
        }

        [Fact]
        public async Task GivenBulkDeleteRequest_WhenJobCreationRequested_ThenJobIsCreated()
        {
            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.HardDelete | DataActions.Delete);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkDelete, Arg.Any<string[]>(), Arg.Any<long?>(), false, false, Arg.Any<CancellationToken>()).Returns(args =>
            {
                var definition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(args.ArgAt<string[]>(1)[0]);
                Assert.Equal(_testUrl, definition.Url);
                Assert.Equal(_testUrl, definition.BaseUrl);
                Assert.Equal(DeleteOperation.HardDelete, definition.DeleteOperation);
                Assert.Equal(searchParams.Count + 1, definition.SearchParameters.Count); // Adds the max time

                return new List<JobInfo>()
                    {
                        new JobInfo()
                        {
                            Id = 1,
                        },
                    };
            });

            var request = new CreateBulkDeleteRequest(DeleteOperation.HardDelete, KnownResourceTypes.Patient, searchParams);

            var response = await _handler.Handle(request, CancellationToken.None);
            Assert.NotNull(response);
            Assert.Equal(1, response.Id);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync((byte)QueueType.BulkDelete, Arg.Any<string[]>(), Arg.Any<long?>(), false, false, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenBulkDeleteRequestWithInvalidSearchParameter_WhenJobCreationRequested_ThenBadRequestIsReturned()
        {
            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.HardDelete | DataActions.Delete);
            _contextAccessor.RequestContext.BundleIssues.Add(new OperationOutcomeIssue(OperationOutcomeConstants.IssueSeverity.Warning, OperationOutcomeConstants.IssueType.Conflict));

            var request = new CreateBulkDeleteRequest(DeleteOperation.HardDelete, KnownResourceTypes.Patient, searchParams);

            await Assert.ThrowsAsync<BadRequestException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Theory]
        [InlineData(DeleteOperation.SoftDelete, DataActions.Read)]
        [InlineData(DeleteOperation.HardDelete, DataActions.Delete)]
        [InlineData(DeleteOperation.PurgeHistory, DataActions.Delete)]
        public async Task GivenUnauthorizedUser_WhenJobCreationRequested_ThenUnauthorizedIsReturned(DeleteOperation deleteOperation, DataActions userRole)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(userRole);

            var request = new CreateBulkDeleteRequest(deleteOperation, null, null);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkDeleteRequest_WhenJobCreationFails_ThenExceptionIsThrown()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.HardDelete | DataActions.Delete);
            _contextAccessor.RequestContext.BundleIssues.Clear();
            _queueClient.EnqueueAsync((byte)QueueType.BulkDelete, Arg.Any<string[]>(), Arg.Any<long?>(), false, false, Arg.Any<CancellationToken>()).Returns(new List<JobInfo>());

            var request = new CreateBulkDeleteRequest(DeleteOperation.HardDelete, null, new List<Tuple<string, string>>());
            await Assert.ThrowsAsync<JobNotExistException>(async () => await _handler.Handle(request, CancellationToken.None));
        }
    }
}
