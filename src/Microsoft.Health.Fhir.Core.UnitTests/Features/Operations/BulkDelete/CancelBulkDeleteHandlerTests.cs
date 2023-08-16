// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Handlers;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkDelete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class CancelBulkDeleteHandlerTests
    {
        private IAuthorizationService<DataActions> _authorizationService;
        private IQueueClient _queueClient;
        private CancelBulkDeleteHandler _handler;

        public CancelBulkDeleteHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _queueClient = Substitute.For<IQueueClient>();
            _handler = new CancelBulkDeleteHandler(_authorizationService, _queueClient, new NullLogger<CancelBulkDeleteHandler>());
        }

        [Fact]
        public async Task GivenBulkDeleteJob_WhenCancelationIsRequested_ThenTheJobIsCancelled()
        {
            await RunBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                    },
                },
                HttpStatusCode.Accepted);
        }

        [Fact]
        public async Task GivenCompletedBulkDeleteJob_WhenCancelationIsRequested_ThenConflictIsReturned()
        {
            await RunBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                    },
                },
                HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task GivenFailedBulkDeleteJob_WhenCancelationIsRequested_ThenConflictIsReturned()
        {
            await RunBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Failed,
                    },
                },
                HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task GivenCancelledBulkDeleteJob_WhenCancelationIsRequested_ThenConflictIsReturned()
        {
            await RunBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Cancelled,
                    },
                },
                HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task GivenUnauthorizedDeleteUser_WhenCancelationIsRequested_ThenUnauthorizedIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Read);

            var request = new CancelBulkDeleteRequest(1);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNonExistantBulkDeleteJob_WhenCancelationIsRequested_ThenNotFoundIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Delete);
            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkDelete, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(new List<JobInfo>());

            var request = new CancelBulkDeleteRequest(1);
            await Assert.ThrowsAsync<JobNotFoundException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        private async Task RunBulkDeleteTest(IReadOnlyList<JobInfo> jobs, HttpStatusCode expectedStatus)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Delete);
            _queueClient.ClearReceivedCalls();

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkDelete, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(jobs);
            var request = new CancelBulkDeleteRequest(1);
            var response = await _handler.Handle(request, CancellationToken.None);

            Assert.Equal(expectedStatus, response.StatusCode);
            if (expectedStatus == HttpStatusCode.Accepted)
            {
                await _queueClient.ReceivedWithAnyArgs(1).CancelJobByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
            }
            else
            {
                await _queueClient.DidNotReceiveWithAnyArgs().CancelJobByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
            }
        }
    }
}
