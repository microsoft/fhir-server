// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator;
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
    public class GetBulkDeleteHandlerTests
    {
        private IAuthorizationService<DataActions> _authorizationService;
        private IQueueClient _queueClient;
        private GetBulkDeleteHandler _handler;

        public GetBulkDeleteHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _queueClient = Substitute.For<IQueueClient>();
            _handler = new GetBulkDeleteHandler(_authorizationService, _queueClient);
        }

        [Fact]
        public async Task GivenCompletedBulkDeleteJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkDeleteResult();
            patientResult1.ResourcesDeleted.Add(KnownResourceTypes.Patient, 15);
            var patientResult2 = new BulkDeleteResult();
            patientResult2.ResourcesDeleted.Add(KnownResourceTypes.Patient, 7);
            var observationResult = new BulkDeleteResult();
            observationResult.ResourcesDeleted.Add(KnownResourceTypes.Observation, 5);

            var resourcesDeleted = new List<Tuple<string, Base>>()
            {
                new Tuple<string, Base>(KnownResourceTypes.Patient, new FhirDecimal(22)),
                new Tuple<string, Base>(KnownResourceTypes.Observation, new FhirDecimal(5)),
            };

            await RunGetBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(patientResult1),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(patientResult2),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(observationResult),
                    },
                },
                new GetBulkDeleteResponse(resourcesDeleted, null, System.Net.HttpStatusCode.OK));
        }

        [Fact]
        public async Task GivenRunningBulkDeleteJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkDeleteResult();
            patientResult1.ResourcesDeleted.Add(KnownResourceTypes.Patient, 15);
            var observationResult = new BulkDeleteResult();
            observationResult.ResourcesDeleted.Add(KnownResourceTypes.Observation, 5);

            var resourcesDeleted = new List<Tuple<string, Base>>()
            {
                new Tuple<string, Base>(KnownResourceTypes.Patient, new FhirDecimal(15)),
                new Tuple<string, Base>(KnownResourceTypes.Observation, new FhirDecimal(5)),
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"),
            };

            await RunGetBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(patientResult1),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(observationResult),
                    },
                },
                new GetBulkDeleteResponse(resourcesDeleted, issues, System.Net.HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task GivenFailedBulkDeleteJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkDeleteResult();
            patientResult1.ResourcesDeleted.Add(KnownResourceTypes.Patient, 15);

            var resourcesDeleted = new List<Tuple<string, Base>>()
            {
                new Tuple<string, Base>(KnownResourceTypes.Patient, new FhirDecimal(15)),
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Encountered an unhandled exception. The job will be marked as failed."),
            };

            await RunGetBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(patientResult1),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Cancelled,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Failed,
                        Result = JsonConvert.SerializeObject(new { message = "Job failed" }),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                },
                new GetBulkDeleteResponse(resourcesDeleted, issues, System.Net.HttpStatusCode.InternalServerError));
        }

        [Fact]
        public async Task GivenCancelledBulkDeleteJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkDeleteResult();
            patientResult1.ResourcesDeleted.Add(KnownResourceTypes.Patient, 15);

            var resourcesDeleted = new List<Tuple<string, Base>>()
            {
                new Tuple<string, Base>(KnownResourceTypes.Patient, new FhirDecimal(15)),
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Warning,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job Canceled"),
            };

            await RunGetBulkDeleteTest(
                new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Completed,
                        Result = JsonConvert.SerializeObject(patientResult1),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Cancelled,
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Running,
                    },
                },
                new GetBulkDeleteResponse(resourcesDeleted, issues, System.Net.HttpStatusCode.OK));
        }

        [Fact]
        public async Task GivenUnauthorizedUser_WhenStatusRequested_ThenUnauthorizedIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var request = new GetBulkDeleteRequest(1);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNonExistantBulkDeleteJob_WhenStatusRequested_ThenNotFoundIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Read);
            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkDelete, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(new List<JobInfo>());

            var request = new GetBulkDeleteRequest(1);
            await Assert.ThrowsAsync<JobNotFoundException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        private async Task RunGetBulkDeleteTest(IReadOnlyList<JobInfo> jobs, GetBulkDeleteResponse expectedResponse)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.Read);

            var definition = JsonConvert.SerializeObject(new BulkDeleteDefinition(JobType.BulkDeleteProcessing, DeleteOperation.HardDelete, null, null, "test", "test"));
            foreach (var job in jobs)
            {
                job.Definition = definition;
            }

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkDelete, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(jobs);
            var request = new GetBulkDeleteRequest(1);
            var response = await _handler.Handle(request, CancellationToken.None);

            Assert.Equal(expectedResponse.HttpStatusCode, response.HttpStatusCode);

            if (expectedResponse.Issues != null)
            {
                Assert.Equal(expectedResponse.Issues.Count, response.Issues.Count);

                foreach (var expectedIssue in expectedResponse.Issues)
                {
                    Assert.True(
                        response.Issues.Any(issue => issue.DetailsText == expectedIssue.DetailsText && issue.Severity == expectedIssue.Severity && issue.Code == expectedIssue.Code),
                        $"Could not find issue with properties: {expectedIssue.Severity} | {expectedIssue.Code} | {expectedIssue.DetailsText}");
                }
            }
            else
            {
                Assert.Null(response.Issues);
            }

            if (expectedResponse.Results != null)
            {
                Assert.Equal(expectedResponse.Results.Count(), response.Results.Count());

                foreach (var result in expectedResponse.Results)
                {
                    Assert.Contains(result, response.Results);
                }
            }
            else
            {
                Assert.Null(response.Results);
            }
        }
    }
}
