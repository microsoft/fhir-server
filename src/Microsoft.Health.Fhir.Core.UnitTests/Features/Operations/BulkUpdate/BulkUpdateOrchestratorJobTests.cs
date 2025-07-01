// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
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
    public class BulkUpdateOrchestratorJobTests
    {
        private IQueueClient _queueClient;
        private ISearchService _searchService;
        private BulkUpdateOrchestratorJob _orchestratorJob;

        public BulkUpdateOrchestratorJobTests()
        {
            _queueClient = Substitute.For<IQueueClient>();
            _searchService = Substitute.For<ISearchService>();
            _orchestratorJob = new BulkUpdateOrchestratorJob(_queueClient, Substitute.For<RequestContextAccessor<IFhirRequestContext>>(), _searchService.CreateMockScopeFactory(), Substitute.For<ILogger<BulkUpdateOrchestratorJob>>());
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsNotGivenAndIsParallelIsTrue_ThenProcessingJobsForAllTypesAreCreatedBasedOnSurrogateIdRanges()
        {
            SetupMockQueue(2);
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            var resourceTypes = new HashSet<string> { "Patient", "Observation" };
            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(resourceTypes.ToList());
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns((x) =>
            {
                var result = new SearchResult(2, new List<Tuple<string, string>>());
                return Task.FromResult(GenerateSearchResult(2));
            });

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, null, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(2).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.ReceivedWithAnyArgs(1).GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that two processing jobs were queued for each type
            var calls = _queueClient.ReceivedCalls();
            var definitions = ((string[])calls.ElementAt(1).GetArguments()[1])
                .Concat((string[])calls.ElementAt(2).GetArguments()[1])
                .ToArray();

            Assert.Equal(4, definitions.Length);

            // Checks that the processing job lists both resource types
            foreach (var definitionString in definitions)
            {
                var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitionString);

                // check actualDefinition.Type contains one of the type from resourceTypes
                Assert.NotNull(actualDefinition.Type);
                Assert.True(resourceTypes.Contains(actualDefinition.Type), $"Expected one of the resource types: {string.Join(", ", resourceTypes)} but got {actualDefinition.Type}.");
                Assert.NotNull(actualDefinition.StartSurrogateId);
                Assert.NotNull(actualDefinition.EndSurrogateId);
            }
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrue_ThenProcessingJobsAreCreatedAtContinuationTokenLevel()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            int callCount = 0;
            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(_ =>
                {
                    callCount++;

                    // First 5 calls: return "continuationToken", 6th call: return null
                    var continuationToken = callCount <= 5 ? "continuationToken" : null;
                    return Task.FromResult(GenerateSearchResult(2, continuationToken));
                });

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(6).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that 6 processing jobs were queued
            var calls = _queueClient.ReceivedCalls();
            var definitions = calls
                .Skip(1) // Skip the first call (index 0)
                .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]

            Assert.Equal(6, definitions.Length);

            for (int i = 0; i < definitions.Length; i++)
            {
                var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[i]);

                // check actualDefinition.Type contains one of the type from resourceTypes
                Assert.NotNull(actualDefinition.Type);
                Assert.Equal("Patient", actualDefinition.Type);
                if (i == 0)
                {
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                }
                else
                {
                    Assert.NotNull(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                }
            }
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueAndSearchReturnMultipageIncludes_ThenProcessingJobsAreCreatedAtContinuationTokenLevelForMatchAndIncludedResources()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            int callCountForMatchResults = 0;
            int callCountForIncludesResults = 0;

            // For match results
            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), false)
                .Returns(_ =>
                {
                    callCountForMatchResults++;

                    // First 5 calls: return "ContinuationToken", 6th call: return null
                    var continuationToken = callCountForMatchResults <= 5 ? "continuationToken" + callCountForMatchResults.ToString() : null;
                    var includesContinuationToken = callCountForMatchResults <= 2 ? "includesContinuationToken" + callCountForMatchResults.ToString() : null;
                    return Task.FromResult(GenerateSearchResult(2, continuationToken, includesContinuationToken));
                });

            // For included results
            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), true)
                .Returns(_ =>
                {
                    callCountForIncludesResults++;

                    // First 2 calls: return "ContinuationToken", 3rd call: return null
                    var continuationToken = "continuationToken";
                    var includesContinuationToken = callCountForIncludesResults <= 2 ? "includesContinuationToken" + callCountForIncludesResults.ToString() : null;
                    return Task.FromResult(GenerateSearchResult(2, continuationToken, includesContinuationToken));
                });

            // Arrange the context to trigger AreIncludeResultsTruncated() == true
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", Core.Resources.TruncatedIncludeMessage),
            };
            var testContext = Substitute.For<IFhirRequestContext>();
            testContext.BundleIssues.Returns(bundleIssues);

            // Use the contextAccessor in the orchestrator
            var orchestratorJobWithTruncatedIssue = new TestBulkUpdateOrchestratorJob(
                _queueClient,
                Substitute.For<RequestContextAccessor<IFhirRequestContext>>(),
                _searchService.CreateMockScopeFactory(),
                Substitute.For<ILogger<BulkUpdateOrchestratorJob>>(),
                testContext);

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await orchestratorJobWithTruncatedIssue.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(10).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that 6 processing jobs were queued
            var calls = _queueClient.ReceivedCalls();
            var definitions = calls
                .Skip(1) // Skip the first call (index 0)
                .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]
            Assert.Equal(10, definitions.Length);

            for (int i = 0; i < definitions.Length; i++)
            {
                var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[i]);

                // check actualDefinition.Type contains one of the type from resourceTypes
                Assert.NotNull(actualDefinition.Type);
                Assert.Equal("Patient", actualDefinition.Type);

                // First call will be normal search with CT and ICT both null
                // The first search call will return ICT, with CT = null, there would be 3 such calls until callCountForIncludesResults reaches 3(no more include results on page 1)
                // We go to next page of match results (callCountForMatchResults = 2) which will have ICT (only 1 included page for 2nd page of matched result page)
                // Remaining pages for match results

                if (i == 0)
                {
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken)).Select(sp => sp.Item2));
                }
                else if (i > 0 && i < 4)
                {
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                    Assert.NotNull(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken)).Select(sp => sp.Item2));
                }
                else if (i == 5)
                {
                    Assert.NotNull(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                    Assert.NotNull(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken)).Select(sp => sp.Item2));
                }
                else
                {
                    Assert.NotNull(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken)).Select(sp => sp.Item2));
                }
            }
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenIsParallelIsFalseWithSearchParameter_ThenOnlyOneProcessingJobsIsCreated()
        {
            SetupMockQueue(2);
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string>()
            {
                "Patient",
                "Observation",
            });
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(2));
            });

            var jobs = new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                    },
                };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), true, Arg.Any<CancellationToken>()).Returns(jobs);

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, searchParams, "test", "test", "test", null, isParallel: false);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that one processing jobs was queued
            var calls = _queueClient.ReceivedCalls();
            var definitions = (string[])calls.ElementAt(1).GetArguments()[1];
            Assert.Single(definitions);

            var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[0]);
            Assert.Null(actualDefinition.Type);
            Assert.Null(actualDefinition.StartSurrogateId);
            Assert.Null(actualDefinition.EndSurrogateId);
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenIsParallelIsFalseWithoutSearchParameter_ThenOnlyOneProcessingJobsIsCreated()
        {
            SetupMockQueue(2);
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string>()
            {
                "Patient",
                "Observation",
            });
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(2));
            });

            var jobs = new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                    },
                };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), true, Arg.Any<CancellationToken>()).Returns(jobs);
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, null, "test", "test", "test", null, isParallel: false);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that one processing jobs was queued
            var calls = _queueClient.ReceivedCalls();
            var definitions = (string[])calls.ElementAt(1).GetArguments()[1];
            Assert.Single(definitions);

            var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[0]);
            Assert.Null(actualDefinition.Type);
            Assert.Null(actualDefinition.StartSurrogateId);
            Assert.Null(actualDefinition.EndSurrogateId);
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenIsParallelIsTrueWithNoSearchParamAndGetUsedResourceTypesReturnsEmpty_ThenNoProcessingJobsAreEnqueued()
        {
            // Arrange
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string>());

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, null, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            // Act
            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Assert
            await _queueClient.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, default, default, default);
            await _searchService.Received(1).GetUsedResourceTypes(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenIsParallelIsTrueWithSearchParamAndSearchReturnsNoResults_ThenNoProcessingJobsAreEnqueued()
        {
            // Arrange
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            var resourceTypes = new List<string> { "Patient" };
            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(resourceTypes);

            // Simulate SearchAsync returning zero results
            _searchService.SearchAsync(
                Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns(Task.FromResult(new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>())));
            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, searchParams, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            // Act
            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Assert
            await _queueClient.DidNotReceiveWithAnyArgs().EnqueueAsync(default, default, default, default, default);
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());
            await _searchService.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>());
        }

        private void SetupMockQueue(int numRanges)
        {
            _searchService.GetSurrogateIdRanges(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                var ranges = new List<(long StartId, long EndId)>();
                if (x.ArgAt<long>(1) <= x.ArgAt<long>(2)) // start <= end to break internal loop
                {
                    for (int i = 0; i < numRanges; i++)
                    {
                        ranges.Add((long.MaxValue - 1, long.MaxValue - 1));
                    }
                }

                return Task.FromResult<IReadOnlyList<(long StartId, long EndId)>>(ranges);
            });
        }

        private static SearchResult GenerateSearchResult(int resultCount, string continuationToken = null, string includesContinuationToken = null)
        {
            var result = new SearchResultEntry(
                new ResourceWrapper(
                    "1234",
                    "1",
                    "Patient",
                    new RawResource(
                        "data",
                        FhirResourceFormat.Unknown,
                        isMetaSet: false),
                    new ResourceRequest("POST"),
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    null));

            var searchResult = new SearchResult(
                Enumerable.Repeat(result, resultCount),
                continuationToken,
                null,
                Array.Empty<Tuple<string, string>>(),
                null,
                includesContinuationToken);
            return searchResult;
        }
    }
}
