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
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Handlers;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate.Messages;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkUpdate
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class GetBulkUpdateHandlerTests
    {
        private readonly string _resourceUpdatedCountLabel = "ResourceUpdatedCount";
        private readonly string _resourceIgnoredCountLabel = "ResourceIgnoredCount";
        private readonly string _resourcePatchFailedCountLabel = "ResourcePatchFailedCount";

        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IQueueClient _queueClient;
        private readonly GetBulkUpdateHandler _handler;

        public GetBulkUpdateHandlerTests()
        {
            _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
            _queueClient = Substitute.For<IQueueClient>();
            _handler = new GetBulkUpdateHandler(_authorizationService, _queueClient);
        }

        [Fact]
        public async Task GivenBulkUpdateJobGroupWithOnlyCreatedJobs_WhenStatusRequested_ThenAcceptedWithNoResults()
        {
            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>();
            var issues = new List<OperationOutcomeIssue>()
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"),
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>()
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Created,
                            Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true)),
                        },
                        0),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task GivenSuccessfullyCompletedBulkUpdateJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkUpdateResult();
            patientResult1.ResourcesUpdated.Add(KnownResourceTypes.Patient, 15);
            patientResult1.ResourcesIgnored.Add(KnownResourceTypes.Practitioner, 1);
            patientResult1.ResourcesIgnored.Add(KnownResourceTypes.Device, 3);

            var patientResult2 = new BulkUpdateResult();
            patientResult2.ResourcesUpdated.Add(KnownResourceTypes.Patient, 7);
            patientResult2.ResourcesIgnored.Add(KnownResourceTypes.Practitioner, 1);

            var observationResult = new BulkUpdateResult();
            observationResult.ResourcesUpdated.Add(KnownResourceTypes.Observation, 5);

            var resourcesUpdated = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(22)),
                new(KnownResourceTypes.Observation, new Integer64(5)),
            };

            var resourcesIgnored = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Practitioner, new Integer64(2)),
                new(KnownResourceTypes.Device, new Integer64(3)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
                { _resourceIgnoredCountLabel, resourcesIgnored },
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(patientResult1),
                        },
                        15),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(patientResult2),
                        },
                        7),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(observationResult),
                        },
                        5),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), null, System.Net.HttpStatusCode.OK));
        }

        [Fact]
        public async Task GivenMixedStatusJobs_WhenStatusRequested_ThenAggregatedStatusIsReturned()
        {
            var completedResult = new BulkUpdateResult();
            completedResult.ResourcesUpdated.Add(KnownResourceTypes.Patient, 10);

            var failedResult = new BulkUpdateResult();
            failedResult.ResourcesUpdated.Add(KnownResourceTypes.Observation, 5);

            var jobs = new List<Tuple<JobInfo, int>>
            {
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(completedResult) }, 10),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Failed, Result = JsonConvert.SerializeObject(failedResult) }, 5),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Running }, 0),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Archived }, 0),
            };

            var resourcesUpdated = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(10)),
                new(KnownResourceTypes.Observation, new Integer64(5)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
            };

            var issues = new List<OperationOutcomeIssue>
            {
                new(OperationOutcomeConstants.IssueSeverity.Error, OperationOutcomeConstants.IssueType.Exception, detailsText: "Encountered an unhandled exception. The job will be marked as failed."),
                new(OperationOutcomeConstants.IssueSeverity.Information, OperationOutcomeConstants.IssueType.Informational, detailsText: "Job In Progress"),
            };

            await RunGetBulkUpdateTest(jobs, new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.InternalServerError));
        }

        [Fact]
        public async Task GivenAllRunningBulkUpdateJobs_WhenStatusRequested_ThenAcceptedWithNoResultsAndInProgressIssue()
        {
            // Arrange: All jobs in the group are in Running status, no results.
            var jobs = new List<Tuple<JobInfo, int>>
            {
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Running }, 0),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Running }, 0),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Running }, 0),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>(); // No results

            var issues = new List<OperationOutcomeIssue>
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"),
            };

            // Act & Assert
            await RunGetBulkUpdateTest(
                jobs,
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task GivenFailedBulkUpdateJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkUpdateResult();
            patientResult1.ResourcesUpdated.Add(KnownResourceTypes.Patient, 15);

            var resourcesUpdated = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(15)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Encountered an unhandled exception. The job will be marked as failed."),
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>()
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(patientResult1),
                        },
                        15),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Failed,
                            Result = JsonConvert.SerializeObject(new { message = "Job failed" }),
                        },
                        0),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.InternalServerError));
        }

        [Fact]
        public async Task GivenRunningBulkUpdateJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkUpdateResult();
            patientResult1.ResourcesUpdated.Add(KnownResourceTypes.Patient, 15);
            patientResult1.ResourcesIgnored.Add(KnownResourceTypes.Practitioner, 1);
            patientResult1.ResourcesIgnored.Add(KnownResourceTypes.Device, 3);

            var observationResult = new BulkUpdateResult();
            observationResult.ResourcesUpdated.Add(KnownResourceTypes.Observation, 5);

            var resourcesUpdated = new List<Tuple<string, Base>>()
            {
                new(KnownResourceTypes.Patient, new Integer64(15)),
                new(KnownResourceTypes.Observation, new Integer64(5)),
            };

            var resourcesIgnored = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Practitioner, new Integer64(1)),
                new(KnownResourceTypes.Device, new Integer64(3)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
                { _resourceIgnoredCountLabel, resourcesIgnored },
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"),
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>()
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(patientResult1),
                        },
                        15),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Running,
                        },
                        0),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Created,
                        },
                        0),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(observationResult),
                        },
                        5),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task GivenSoftFailedBulkUpdateJob_WhenStatusRequestedAndJobIsRunning_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkUpdateResult();
            patientResult1.ResourcesUpdated.Add(KnownResourceTypes.Patient, 15);
            patientResult1.ResourcesPatchFailed.Add(KnownResourceTypes.Patient, 5);
            patientResult1.ResourcesPatchFailed.Add(KnownResourceTypes.Observation, 3);

            var resourcesUpdated = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(15)),
            };

            var resourcesPatchFailed = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(5)),
                new(KnownResourceTypes.Observation, new Integer64(3)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
                { _resourcePatchFailedCountLabel, resourcesPatchFailed },
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Encountered an unhandled exception. The job will be marked as failed."),
                new(
                    OperationOutcomeConstants.IssueSeverity.Information,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job In Progress"),
                new(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Please use FHIR Patch endpoint for detailed error on Patch failed resources."),
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>()
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Failed,
                            Result = JsonConvert.SerializeObject(patientResult1),
                        },
                        15),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Running,
                        },
                        0),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Running,
                        },
                        0),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.Accepted));
        }

        [Fact]
        public async Task GivenEmptyResultsJob_WhenStatusRequested_ThenReturnsNoResults()
        {
            var result = new BulkUpdateResult(); // No resources updated/ignored/failed
            var jobs = new List<Tuple<JobInfo, int>>
            {
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(result) }, 0),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>(); // Empty

            await RunGetBulkUpdateTest(jobs, new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), null, System.Net.HttpStatusCode.OK));
        }

        [Fact]
        public async Task GivenJobWithNullOrMissingDefinitionOrResult_WhenStatusRequested_ThenHandlesGracefully()
        {
            var jobs = new List<Tuple<JobInfo, int>>
            {
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Completed, Result = null }, 0),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Completed }, 0),
                new Tuple<JobInfo, int>(new JobInfo { Status = JobStatus.Completed, Definition = null }, 0),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>(); // Empty

            await RunGetBulkUpdateTest(jobs, new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), null, System.Net.HttpStatusCode.OK));
        }

        [Fact]
        public async Task GivenSoftFailedBulkUpdateJob_WhenStatusRequestedAndJobIsComplete_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkUpdateResult();
            patientResult1.ResourcesUpdated.Add(KnownResourceTypes.Patient, 15);
            patientResult1.ResourcesPatchFailed.Add(KnownResourceTypes.Patient, 5);

            var observationResult = new BulkUpdateResult();
            observationResult.ResourcesUpdated.Add(KnownResourceTypes.Observation, 5);

            var resourcesUpdated = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(15)),
                new(KnownResourceTypes.Observation, new Integer64(5)),
            };

            var resourcesPatchFailed = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Patient, new Integer64(5)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
                { _resourcePatchFailedCountLabel, resourcesPatchFailed },
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Encountered an unhandled exception. The job will be marked as failed."),
                new(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    detailsText: "Please use FHIR Patch endpoint for detailed error on Patch failed resources."),
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>()
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Failed,
                            Result = JsonConvert.SerializeObject(patientResult1),
                        },
                        15),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(observationResult),
                        },
                        15),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.InternalServerError));
        }

        [Fact]
        public async Task GivenCancelledBulkUpdateJob_WhenStatusRequested_ThenStatusIsReturned()
        {
            var patientResult1 = new BulkUpdateResult();
            patientResult1.ResourcesUpdated.Add(KnownResourceTypes.Patient, 15);
            patientResult1.ResourcesIgnored.Add(KnownResourceTypes.Practitioner, 2);
            patientResult1.ResourcesIgnored.Add(KnownResourceTypes.Device, 3);

            var resourcesUpdated = new List<Tuple<string, Base>>()
            {
                new(KnownResourceTypes.Patient, new Integer64(15)),
            };

            var resourcesIgnored = new List<Tuple<string, Base>>
            {
                new(KnownResourceTypes.Practitioner, new Integer64(2)),
                new(KnownResourceTypes.Device, new Integer64(3)),
            };

            var resultsDictionary = new Dictionary<string, ICollection<Tuple<string, Base>>>()
            {
                { _resourceUpdatedCountLabel, resourcesUpdated },
                { _resourceIgnoredCountLabel, resourcesIgnored },
            };

            var issues = new List<OperationOutcomeIssue>()
            {
                new(
                    OperationOutcomeConstants.IssueSeverity.Warning,
                    OperationOutcomeConstants.IssueType.Informational,
                    detailsText: "Job Canceled"),
            };

            await RunGetBulkUpdateTest(
                new List<Tuple<JobInfo, int>>()
                {
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Completed,
                            Result = JsonConvert.SerializeObject(patientResult1),
                        },
                        15),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Cancelled,
                        },
                        0),
                    new Tuple<JobInfo, int>(
                        new()
                        {
                            Status = JobStatus.Running,
                        },
                        0),
                },
                new GetBulkUpdateResponse(ToParameters(resultsDictionary).ToArray(), issues, System.Net.HttpStatusCode.OK));
        }

        [Fact]
        public async Task GivenUnauthorizedUser_WhenStatusRequested_ThenUnauthorizedIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.None);

            var request = new GetBulkUpdateRequest(1);
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNonExistantBulkUpdateJob_WhenStatusRequested_ThenNotFoundIsReturned()
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);
            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), false, Arg.Any<CancellationToken>()).Returns(new List<JobInfo>());

            var request = new GetBulkUpdateRequest(1);
            await Assert.ThrowsAsync<JobNotFoundException>(async () => await _handler.Handle(request, CancellationToken.None));
        }

        private async Task RunGetBulkUpdateTest(IReadOnlyList<Tuple<JobInfo, int>> jobs, GetBulkUpdateResponse expectedResponse)
        {
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.BulkOperator);

            foreach (var job in jobs)
            {
                var definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, null, null, "test", "test", "test", null, isParallel: true));
                job.Item1.Definition = definition;
            }

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), true, Arg.Any<CancellationToken>()).Returns(jobs.Select(job => job.Item1).ToList());
            var request = new GetBulkUpdateRequest(1);
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
                Assert.Empty(response.Issues);
            }

            if (expectedResponse.Results != null)
            {
                Assert.Equal(expectedResponse.Results.Count, response.Results.Count);

                foreach (var tuple in expectedResponse.Results.Zip(response.Results))
                {
                    Assert.True(tuple.First.Matches(tuple.Second));
                }
            }
            else
            {
                Assert.Null(response.Results);
            }
        }

        private static ICollection<Parameters.ParameterComponent> ToParameters(Dictionary<string, ICollection<Tuple<string, Base>>> dictionary)
        {
            var list = new List<Parameters.ParameterComponent>();

            foreach (KeyValuePair<string, ICollection<Tuple<string, Base>>> pair in dictionary)
            {
                var parameterComponent = new Parameters.ParameterComponent
                {
                    Name = pair.Key,
                };

                parameterComponent.Part.AddRange(
                    pair.Value.Select(part => new Parameters.ParameterComponent
                    {
                        Name = part.Item1,
                        Value = (DataType)part.Item2,
                    }));

                list.Add(parameterComponent);
            }

            return list;
        }
    }
}
