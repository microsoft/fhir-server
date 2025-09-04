// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
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
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkUpdate
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class BulkUpdateOrchestratorJobTests
    {
        private readonly IQueueClient _queueClient;
        private readonly ISearchService _searchService;
        private readonly BulkUpdateOrchestratorJob _orchestratorJob;

        public BulkUpdateOrchestratorJobTests()
        {
            _queueClient = Substitute.For<IQueueClient>();
            _searchService = Substitute.For<ISearchService>();
            _orchestratorJob = new BulkUpdateOrchestratorJob(_queueClient, Substitute.For<RequestContextAccessor<IFhirRequestContext>>(), _searchService.CreateMockScopeFactory(), Substitute.For<ILogger<BulkUpdateOrchestratorJob>>());
        }

        [Theory]
        [MemberData(nameof(GetAllowedSearchParameters))]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsNotGivenOrAreAllowedAndIsParallelIsTrue_ThenProcessingJobsForAllTypesAreCreatedBasedOnSurrogateIdRanges(List<Tuple<string, string>> searchParams)
        {
            SetupMockQueue(2);
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            var resourceTypes = new HashSet<string> { "Patient", "Observation" };
            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(resourceTypes.ToList());
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(2));
            });

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, searchParams, "test", "test", "test", null, isParallel: true);
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
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsNotGivenOrAreAllowedAndIsParallelIsTrueAndExistingEnqueuedJobs_ThenProcessingJobsForAllTypesAreCreatedBasedOnSurrogateIdRanges()
        {
            SetupMockQueue(2);
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("_lastUpdated", "value"),
            };

            var resourceTypes = new HashSet<string> { "Patient", "Observation" };
            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(resourceTypes.ToList());
            _searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>()).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(2));
            });

            var dateTimeNow = DateTime.UtcNow;
            var jobs = new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 1,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true, false, null, null)),
                        CreateDate = dateTimeNow.AddSeconds(-60),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 2,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "Patient", searchParams, "test", "test", "test", null, isParallel: true, false, "21", "31")),
                        CreateDate = dateTimeNow.AddSeconds(-30),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 3,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "Patient", searchParams, "test", "test", "test", null, isParallel: true, false, "21", (long.MaxValue - 3).ToString())),
                        CreateDate = dateTimeNow.AddSeconds(-15),
                    },
                };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), true, Arg.Any<CancellationToken>()).Returns(jobs);
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, searchParams, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Only one call for resource type Observation as Patient has existing jobs covering the surrogate id ranges
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.ReceivedWithAnyArgs(1).GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that two processing jobs were queued for each type
            var calls = _queueClient.ReceivedCalls();
            var definitions = ((string[])calls.ElementAt(1).GetArguments()[1]).ToArray();

            Assert.Equal(2, definitions.Length);

            // Checks that the processing job lists both resource types
            foreach (var definitionString in definitions)
            {
                var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitionString);

                // check actualDefinition.Type contains one of the type from resourceTypes
                Assert.NotNull(actualDefinition.Type);
                Assert.Equal("Observation", actualDefinition.Type);
                Assert.NotNull(actualDefinition.StartSurrogateId);
                Assert.NotNull(actualDefinition.EndSurrogateId);
            }
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueWithSinglePageResults_ThenProcessingJobsAreCreatedAtContinuationTokenLevel()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(_ =>
                {
                    return Task.FromResult(GenerateSearchResult(2, null));
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
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that 1 processing job was queued
            var calls = _queueClient.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IQueueClient.EnqueueAsync))
                .ToList();
            var definitions = calls
                 .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]

            Assert.Single(definitions);
            var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[0]);

            // check actualDefinition.Type contains one of the type from resourceTypes
            Assert.NotNull(actualDefinition.Type);
            Assert.Equal("Patient", actualDefinition.Type);
            Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueWithMultiplePageResultsAndPartiallyEnqueuedJobs_ThenProcessingJobsAreCreatedAtContinuationTokenLevelForRemainingPagesOnly()
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

            var dateTimeNow = DateTime.UtcNow;
            var jobs = new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 1,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true)),
                        CreateDate = dateTimeNow.AddSeconds(-60),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 2,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "Patient", new List<Tuple<string, string>>() { new Tuple<string, string>("ct", "21"), }, "test", "test", "test", null, isParallel: true)),
                        CreateDate = dateTimeNow.AddSeconds(-30),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 3,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "Patient", new List<Tuple<string, string>>() { new Tuple<string, string>("ct", "22"), }, "test", "test", "test", null, isParallel: true)),
                        CreateDate = dateTimeNow.AddSeconds(-15),
                    },
                };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), true, Arg.Any<CancellationToken>()).Returns(jobs);
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.ReceivedWithAnyArgs(5).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that 5 processing jobs were queued
            // Only 5 jobs were enqueued as first call to search is going to check for previously enqueued jobs, so we return continuation token for next page
            var calls = _queueClient.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IQueueClient.EnqueueAsync))
                .ToList();
            var definitions = calls
                .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]

            Assert.Equal(5, definitions.Length);

            for (int i = 0; i < definitions.Length; i++)
            {
                var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[i]);

                // check actualDefinition.Type contains one of the type from resourceTypes
                Assert.NotNull(actualDefinition.Type);
                Assert.Equal("Patient", actualDefinition.Type);

                // Since 2 jobs already exist for continuationToken 21 and 22, the new ones should be for rest of the pages
                // Continuation token is not null for the first call as we start from page 3
                Assert.NotNull(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
            }
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueWithMultiplePageResultsAndCompletelyEnqueuedJobs_ThenProcessingJobsAreCreatedAtContinuationTokenLevelForRemainingPagesOnly()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(_ =>
                {
                    // no continuation token as a last page
                    return Task.FromResult(GenerateSearchResult(2, null));
                });

            var searchParams = new List<Tuple<string, string>>()
            {
                new Tuple<string, string>("param", "value"),
            };

            var dateTimeNow = DateTime.UtcNow;
            var jobs = new List<JobInfo>()
                {
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 1,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true)),
                        CreateDate = dateTimeNow.AddSeconds(-60),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 2,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "Patient", new List<Tuple<string, string>>() { new Tuple<string, string>("ct", "21"), }, "test", "test", "test", null, isParallel: true)),
                        CreateDate = dateTimeNow.AddSeconds(-30),
                    },
                    new JobInfo()
                    {
                        Status = JobStatus.Created,
                        GroupId = 1,
                        Id = 3,
                        Definition = JsonConvert.SerializeObject(new BulkUpdateDefinition(JobType.BulkUpdateProcessing, "Patient", new List<Tuple<string, string>>() { new Tuple<string, string>("ct", "22"), }, "test", "test", "test", null, isParallel: true)),
                        CreateDate = dateTimeNow.AddSeconds(-15),
                    },
                };

            _queueClient.GetJobByGroupIdAsync((byte)QueueType.BulkUpdate, Arg.Any<long>(), true, Arg.Any<CancellationToken>()).Returns(jobs);
            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, "Patient", searchParams, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Id = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None);
            await _queueClient.DidNotReceiveWithAnyArgs().EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that 0 processing job was queued
            var calls = _queueClient.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IQueueClient.EnqueueAsync))
                .ToList();
            var definitions = calls
                .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]

            Assert.Empty(definitions);
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueWithMultiplePageResults_ThenProcessingJobsAreCreatedAtContinuationTokenLevel()
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
            var calls = _queueClient.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IQueueClient.EnqueueAsync))
                .ToList();
            var definitions = calls
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
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueAndSearchReturnSingleIncludePage_ThenProcessingJobsAreCreatedAtContinuationTokenLevelForMatchResourcesOnly()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            // For match results
            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), false)
                .Returns(_ =>
                {
                    // First page with no more matched results but has includes continuation token
                    return Task.FromResult(GenerateSearchResult(2, null, "includesContinuationToken"));
                });

            // For included results
            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), true)
                .Returns(_ =>
                {
                    // When call for includes, it returns a single page with no continuation token
                    return Task.FromResult(GenerateSearchResult(2, null, null));
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
            await _queueClient.ReceivedWithAnyArgs(1).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that only 1 processing jobs was queued
            var calls = _queueClient.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IQueueClient.EnqueueAsync))
                .ToList();
            var definitions = calls
                .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]
            Assert.Single(definitions);

            var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[0]);

            // check actualDefinition.Type contains one of the type from resourceTypes
            Assert.NotNull(actualDefinition.Type);
            Assert.Equal("Patient", actualDefinition.Type);

            // First call will be normal search with CT and ICT both null
            Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
            Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken)).Select(sp => sp.Item2));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchParameterIsGivenAndIsParallelIsTrueAndSearchReturnMultipageIncludesPages_ThenProcessingJobsAreCreatedAtContinuationTokenLevelForMatchResourcesOnly()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            int callCountForMatchResults = 0;
            _searchService
                .SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), false)
                .Returns(_ =>
                {
                    callCountForMatchResults++;

                    // First 3 calls: return "ContinuationToken", 4th call: return null
                    // Total 4 matched pages, with the first 2 pages having "includesContinuationToken"
                    var continuationToken = callCountForMatchResults <= 3 ? "continuationToken" : null;
                    var includesContinuationToken = callCountForMatchResults <= 2 ? "includesContinuationToken" : null;
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
            await _queueClient.ReceivedWithAnyArgs(4).EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>());
            await _searchService.DidNotReceiveWithAnyArgs().GetUsedResourceTypes(Arg.Any<CancellationToken>());

            // Checks that 4 processing jobs were queued for 4 matched result pages
            var calls = _queueClient.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(IQueueClient.EnqueueAsync))
                .ToList();
            var definitions = calls
                .Select(call => (string[])call.GetArguments()[1]) // Get the definitions array from each call
                .SelectMany(defs => defs) // Flatten all arrays into one sequence
                .ToArray(); // Convert to a single string[]
            Assert.Equal(4, definitions.Length);

            for (int i = 0; i < definitions.Length; i++)
            {
                var actualDefinition = JsonConvert.DeserializeObject<BulkUpdateDefinition>(definitions[i]);

                // check actualDefinition.Type contains one of the type from resourceTypes
                Assert.NotNull(actualDefinition.Type);
                Assert.Equal("Patient", actualDefinition.Type);

                // Just read all the matched pages
                // MatchPage1 hence 1st call equeued as (ct=null,ict=null)
                // MatchPage2 hence 2nd call (ct=continuationToken, ict=null)
                // MatchPage3 hence 3rd call (ct=continuationToken, ict=null)
                // MatchPage4 hence 4th call (ct=continuationToken, ict=null)

                if (i == 0)
                {
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.ContinuationToken)).Select(sp => sp.Item2));
                    Assert.Empty(actualDefinition.SearchParameters.Where(sp => sp.Item1.Equals(KnownQueryParameterNames.IncludesContinuationToken)).Select(sp => sp.Item2));
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
                        GroupId = 1,
                        Id = 1,
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
        public async Task GivenBulkUpdateJob_WhenIsParallelIsTrueWithoutSearchParamAndGetUsedResourceTypesReturnsEmpty_ThenNoProcessingJobsAreEnqueued()
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

        [Fact]
        public async Task GivenBulkUpdateJob_WhenQueueClientThrowsException_ThenExceptionIsPropagated()
        {
            SetupMockQueue(2);
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>()).Returns(new List<string> { "Patient" });
            _queueClient.EnqueueAsync(Arg.Any<byte>(), Arg.Any<string[]>(), Arg.Any<long?>(), false, Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("Unexpected error"));

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, null, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenSearchServiceThrowsException_ThenExceptionIsPropagated()
        {
            _queueClient.ClearReceivedCalls();
            _searchService.ClearReceivedCalls();

            _searchService.GetUsedResourceTypes(Arg.Any<CancellationToken>())
                .Throws(new InvalidOperationException("Search error"));

            var definition = new BulkUpdateDefinition(JobType.BulkUpdateOrchestrator, null, null, "test", "test", "test", null, isParallel: true);
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = JsonConvert.SerializeObject(definition),
                CreateDate = DateTime.Now,
            };

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenJobInfoIsNull_ThenArgumentNullExceptionIsThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await _orchestratorJob.ExecuteAsync(null, CancellationToken.None));
        }

        [Fact]
        public async Task GivenBulkUpdateJob_WhenJobDefinitionIsMalformed_ThenExceptionIsPropagated()
        {
            var jobInfo = new JobInfo()
            {
                GroupId = 1,
                Definition = "not a valid json",
                CreateDate = DateTime.Now,
            };

            await Assert.ThrowsAsync<JsonReaderException>(async () => await _orchestratorJob.ExecuteAsync(jobInfo, CancellationToken.None));
        }

        public static IEnumerable<object[]> GetAllowedSearchParameters()
        {
            yield return new object[]
            {
                new List<Tuple<string, string>>(),
            };
            yield return new object[]
            {
                new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("_lastUpdated", "value"),
                },
            };
            yield return new object[]
            {
                new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("_maxCount", "value"),
                },
            };
            yield return new object[]
            {
                new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("_lastUpdated", "value"),
                    new Tuple<string, string>("_maxCount", "value"),
                },
            };
            yield return new object[]
            {
                new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("_lastUpdated", "value1"),
                    new Tuple<string, string>("_lastUpdated", "value2"),
                    new Tuple<string, string>("_lastUpdated", "value3"),
                },
            };
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
                        ranges.Add((long.MaxValue - i - 100, long.MaxValue - i - 50));
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
