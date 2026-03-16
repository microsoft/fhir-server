// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkUpdate
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class CancelBulkUpdateHandlerTests
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly CancelBulkUpdateHandler _handler;

        public CancelBulkUpdateHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _queueClient = Substitute.For<IQueueClient>();
            _supportedProfiles = Substitute.For<ISupportedProfilesStore>();
            _handler = new CancelBulkUpdateHandler(_authorizationService, _queueClient, _supportedProfiles, new NullLogger<CancelBulkUpdateHandler>());
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenCancelationIsRequested_ThenTheJobIsCancelled()
        {
            await RunBulkUpdateTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                    },
                },
                HttpStatusCode.Accepted);
        }

        [Theory]
        [MemberData(nameof(BulkUpdateJobConflictTestData))]
        public async Task GivenBulkUpdateJob_WhenCancelationIsRequested_ThenConflictIsReturned(IReadOnlyList<JobInfo> jobInfo)
        {
            await RunBulkUpdateTest(jobInfo, HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task GivenSoftFailedBulkUpdateJobWithOtherJobRunning_WhenCancelationIsRequested_ThenTheJobIsAccepted()
        {
            var bulkUpdateResult = new BulkUpdateResult();
            bulkUpdateResult.ResourcesUpdated.Add("Patient", 1);
            bulkUpdateResult.ResourcesPatchFailed.Add("Patient", 1);
            await RunBulkUpdateTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Failed,
                        Result = JsonConvert.SerializeObject(bulkUpdateResult),
                    },
                },
                HttpStatusCode.Accepted);
        }

        [Fact]
        public async Task GivenCompletedJobWithProfileResourceUpdate_WhenCancelationIsRequested_ThenProfilesAreRefreshed()
        {
            var profileTypes = new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" };
            _supportedProfiles.GetProfilesTypes().Returns(profileTypes);

            var bulkUpdateResult = new BulkUpdateResult();
            bulkUpdateResult.ResourcesUpdated.Add("ValueSet", 1);
            bulkUpdateResult.ResourcesUpdated.Add("Patient", 1);
            var jobInfo = new List<JobInfo>()
            {
                new JobInfo()
                {
                    Status = JobStatus.Running,
                },
                new JobInfo()
                {
                    Status = JobStatus.Completed,
                    Result = JsonConvert.SerializeObject(bulkUpdateResult),
                },
            };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>())
                .Returns(jobInfo);

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>())
                .Returns(DataActions.BulkOperator);

            var request = new CancelBulkUpdateRequest(1);
            await _handler.Handle(request, CancellationToken.None);

            _supportedProfiles.Received(1).Refresh();
        }

        [Fact]
        public async Task GivenSoftFailedJobWithProfileResourceUpdate_WhenCancelationIsRequested_ThenProfilesAreRefreshed()
        {
            var profileTypes = new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" };
            _supportedProfiles.GetProfilesTypes().Returns(profileTypes);

            var bulkUpdateResult = new BulkUpdateResult();
            bulkUpdateResult.ResourcesUpdated.Add("ValueSet", 1);
            bulkUpdateResult.ResourcesUpdated.Add("Patient", 1);
            bulkUpdateResult.ResourcesPatchFailed.Add("Patient", 1);
            var jobInfo = new List<JobInfo>()
            {
                new JobInfo()
                {
                    Status = JobStatus.Running,
                },
                new JobInfo()
                {
                    Status = JobStatus.Failed,
                    Result = JsonConvert.SerializeObject(bulkUpdateResult),
                },
            };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>())
                .Returns(jobInfo);

            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>())
                .Returns(DataActions.BulkOperator);

            var request = new CancelBulkUpdateRequest(1);
            await _handler.Handle(request, CancellationToken.None);

            _supportedProfiles.Received(1).Refresh();
        }

        [Fact]
        public async Task GivenUnauthorizedDeleteUser_WhenCancelationIsRequested_ThenUnauthorizedIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Write);

            var request = new CancelBulkUpdateRequest(1);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNonExistantBulkUpdateJob_WhenCancelationIsRequested_ThenNotFoundIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(new List<JobInfo>());

            var request = new CancelBulkUpdateRequest(1);
            await Assert.ThrowsAsync<JobNotFoundException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenQueueClientThrowsException_ThenExceptionIsPropagated()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>())
                .Throws(new System.InvalidOperationException("Unexpected error"));

            var request = new CancelBulkUpdateRequest(1);
            await Assert.ThrowsAsync<System.InvalidOperationException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenUserHasPartialAccess_ThenUnauthorizedFhirActionExceptionIsThrown()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Read);

            var request = new CancelBulkUpdateRequest(1);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateJobWithNoJobsInGroup_WhenCancelationIsRequested_ThenNotFoundIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>())
                .Returns(new List<JobInfo>());

            var request = new CancelBulkUpdateRequest(1);
            await Assert.ThrowsAsync<JobNotFoundException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        public static IEnumerable<object[]> BulkUpdateJobConflictTestData()
        {
            // Test 1: Cancelled job
            var bulkUpdateResult = new BulkUpdateResult();
            bulkUpdateResult.ResourcesUpdated.Add("Patient", 1);
            bulkUpdateResult.ResourcesIgnored.Add("Observation", 1);

            yield return new object[]
            {
                new List<JobInfo>
                {
                    new JobInfo
                    {
                        Status = JobStatus.Cancelled,
                        Result = null,
                    },
                },
            };

            // Test 2: Completed job
            yield return new object[]
            {
                new List<JobInfo>
                {
                    new JobInfo
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(bulkUpdateResult),
                    },
                },
            };

            // Test 3: Failed jobs
            var bulkUpdateResultSoftFailed = new BulkUpdateResult();
            bulkUpdateResultSoftFailed.ResourcesUpdated.Add("Patient", 1);
            bulkUpdateResultSoftFailed.ResourcesPatchFailed.Add("Patient", 1);

            var bulkUpdateResultFailed = new BulkUpdateResult();
            bulkUpdateResultFailed.Issues.Add("Encountered an unhandled exception. The job will be marked as failed.");

            yield return new object[]
            {
                new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(bulkUpdateResult) },
                    new JobInfo { Status = JobStatus.Failed },
                    new JobInfo { Status = JobStatus.Failed, Result = JsonConvert.SerializeObject(bulkUpdateResultFailed) },
                    new JobInfo { Status = JobStatus.Failed, Result = "SQL exception error" },
                    new JobInfo { Status = JobStatus.Failed, Result = JsonConvert.SerializeObject(bulkUpdateResultSoftFailed) },
                },
            };

            // Test 4: Soft failed with completed
            yield return new object[]
            {
                new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(bulkUpdateResult) },
                    new JobInfo { Status = JobStatus.Failed, Result = JsonConvert.SerializeObject(bulkUpdateResultSoftFailed) },
                },
            };
        }

        private async Task RunBulkUpdateTest(IReadOnlyList<JobInfo> jobs, HttpStatusCode expectedStatus)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _queueClient.ClearReceivedCalls();

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(jobs);
            var request = new CancelBulkUpdateRequest(1);

            if (expectedStatus == HttpStatusCode.Conflict)
            {
                OperationFailedException operationFailedException = await Assert.ThrowsAsync<OperationFailedException>(async () => await _handler.Handle(request, CancellationToken.None));
                Assert.Equal(HttpStatusCode.Conflict, operationFailedException.ResponseStatusCode);
            }
            else
            {
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
}
