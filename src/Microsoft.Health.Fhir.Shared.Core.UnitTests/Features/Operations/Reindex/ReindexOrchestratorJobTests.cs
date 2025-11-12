// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [CollectionDefinition("ReindexOrchestratorJobTests")]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexOrchestratorJobTests : IClassFixture<SearchParameterFixtureData>
    {
        private const int _mockedSearchCount = 5;

        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private ISearchParameterStatusManager _searchParameterStatusmanager;
        private Func<ReindexOrchestratorJob> _reindexOrchestratorJobTaskFactory;
        private Func<ReindexProcessingJob> _reindexProcessingJobTaskFactory;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly IQueueClient _queueClient = new TestQueueClient();

        private ISearchParameterDefinitionManager _searchDefinitionManager;
        private CancellationToken _cancellationToken;

        public ReindexOrchestratorJobTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            _searchDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterStatusmanager = Substitute.For<ISearchParameterStatusManager>();

            _searchParameterOperations = Substitute.For<ISearchParameterOperations>();

            var job = CreateReindexJobRecord();
            List<SearchParameterInfo> searchParameterInfos = new List<SearchParameterInfo>()
            {
                new SearchParameterInfo(
                    "Account",
                    "status",
                    ValueSets.SearchParamType.Token,
                    url: new Uri("http://hl7.org/fhir/SearchParameter/Account-status"),
                    baseResourceTypes: new List<string>()
                    {
                        "Account",
                        "_count",
                        "_type",
                    })
                {
                    IsSearchable = true,
                    SearchParameterStatus = SearchParameterStatus.Enabled,
                },
            };
            ReadOnlyCollection<ResourceSearchParameterStatus> status = new ReadOnlyCollection<ResourceSearchParameterStatus>(new List<ResourceSearchParameterStatus>()
            {
                new ResourceSearchParameterStatus()
                {
                    LastUpdated = DateTime.UtcNow,
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Account-status"),
                    Status = SearchParameterStatus.Enabled,
                },
            });

            // Fix: Properly set up the mock with explicit CancellationToken argument matching
            _searchParameterStatusmanager
                .GetAllSearchParameterStatus(_cancellationToken)
                .Returns(status);

            _searchDefinitionManager.AllSearchParameters.Returns(searchParameterInfos);

            IFhirRuntimeConfiguration fhirRuntimeConfiguration = new AzureHealthDataServicesRuntimeConfiguration();

            _reindexOrchestratorJobTaskFactory = () =>
            {
                // Create a mock CoreFeatureConfiguration for the test
                var coreFeatureConfig = Substitute.For<IOptions<CoreFeatureConfiguration>>();
                coreFeatureConfig.Value.Returns(new CoreFeatureConfiguration
                {
                    SearchParameterCacheRefreshIntervalSeconds = 1, // Use a short interval for tests
                });

                // Create a mock OperationsConfiguration for the test
                var operationsConfig = Substitute.For<IOptions<OperationsConfiguration>>();
                operationsConfig.Value.Returns(new OperationsConfiguration
                {
                    Reindex = new ReindexJobConfiguration
                    {
                        ReindexDelayMultiplier = 1, // Use a short multiplier for tests
                    },
                });

                return new ReindexOrchestratorJob(
                     _queueClient,
                     () => _searchService.CreateMockScope(),
                     _searchDefinitionManager,
                     ModelInfoProvider.Instance,
                     _searchParameterStatusmanager,
                     _searchParameterOperations,
                     fhirRuntimeConfiguration,
                     NullLoggerFactory.Instance,
                     coreFeatureConfig,
                     operationsConfig);
            };
        }

        [Theory]
        [InlineData(DataStore.SqlServer)]
        [InlineData(DataStore.CosmosDb)]
        public async Task GivenSupportedParams_WhenExecuted_ThenCorrectSearchIsPerformed(DataStore dataStore)
        {
            const int maxNumberOfResourcesPerQuery = 100;
            const int startResourceSurrogateId = 1;
            const int endResourceSurrogateId = 1000;
            const string resourceTypeHash = "accountHash";

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(resourceTypeHash);

            IFhirRuntimeConfiguration fhirRuntimeConfiguration = dataStore == DataStore.SqlServer ?
                new AzureHealthDataServicesRuntimeConfiguration() :
                new AzureApiForFhirRuntimeConfiguration();

            _searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(new List<(long, long)>
                {
                    (startResourceSurrogateId, endResourceSurrogateId),
                });

            // Get one search parameter and configure it such that it needs to be reindexed
            var param = new SearchParameterInfo(
                "Account",
                "status",
                ValueSets.SearchParamType.Token,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Account-status"),
                baseResourceTypes: new List<string>() { "Account" }) // Fix: Only include valid resource types
            {
                IsSearchable = true,
                SearchParameterStatus = SearchParameterStatus.Supported, // This parameter needs reindexing
            };

            // Create search parameter status that indicates it needs reindexing
            ReadOnlyCollection<ResourceSearchParameterStatus> status = new ReadOnlyCollection<ResourceSearchParameterStatus>(new List<ResourceSearchParameterStatus>()
            {
                new ResourceSearchParameterStatus()
                {
                    LastUpdated = DateTime.UtcNow,
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Account-status"),
                    Status = SearchParameterStatus.Supported,
                },
            });
            var searchParameterStatusmanager = Substitute.For<ISearchParameterStatusManager>();
            searchParameterStatusmanager.GetAllSearchParameterStatus(Arg.Any<CancellationToken>()).Returns(status);

            // Set up the search definition manager properly
            var searchDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchDefinitionManager.AllSearchParameters.Returns(new List<SearchParameterInfo> { param });

            // Mock GetSearchParameters to return the Account-status parameter for "Account" resource type
            searchDefinitionManager.GetSearchParameters("Account").Returns(new List<SearchParameterInfo> { param });

            // Mock GetSearchParameter to return the specific parameter by URL
            searchDefinitionManager.GetSearchParameter("http://hl7.org/fhir/SearchParameter/Account-status").Returns(param);

            // Create mock dependencies for ReindexProcessingJob
            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();

            // Set up the factory for ReindexProcessingJob
            _reindexProcessingJobTaskFactory = () => new ReindexProcessingJob(
                () => _searchService.CreateMockScope(),
                () => fhirDataStore.CreateMockScope(),
                resourceWrapperFactory,
                _searchParameterOperations,
                NullLogger<ReindexProcessingJob>.Instance);

            var reindexOrchestratorJobTaskFactory = () =>
            {
                // Create a mock CoreFeatureConfiguration for the test
                var coreFeatureConfig = Substitute.For<IOptions<CoreFeatureConfiguration>>();
                coreFeatureConfig.Value.Returns(new CoreFeatureConfiguration
                {
                    SearchParameterCacheRefreshIntervalSeconds = 1, // Use a short interval for tests
                });

                // Create a mock OperationsConfiguration for the test
                var operationsConfig = Substitute.For<IOptions<OperationsConfiguration>>();
                operationsConfig.Value.Returns(new OperationsConfiguration
                {
                    Reindex = new ReindexJobConfiguration
                    {
                        ReindexDelayMultiplier = 1, // Use a short multiplier for tests
                    },
                });

                return new ReindexOrchestratorJob(
                    _queueClient,
                    () => _searchService.CreateMockScope(),
                    searchDefinitionManager,
                    ModelInfoProvider.Instance,
                    searchParameterStatusmanager,
                    _searchParameterOperations,
                    fhirRuntimeConfiguration,
                    NullLoggerFactory.Instance,
                    coreFeatureConfig,
                    operationsConfig);
            };

            var expectedResourceType = "Account"; // Fix: Use the actual resource type

            // Fix: Create a proper ReindexJobRecord (this was the main issue)
            ReindexJobRecord job = new ReindexJobRecord(
                new Dictionary<string, string>() { { "Account", resourceTypeHash } }, // Resource type hash map
                new List<string>(), // No specific target resource types (will process all applicable)
                new List<string>(), // No specific target search parameter types
                new List<string>(), // No specific search parameter resource types
                maxNumberOfResourcesPerQuery); // Max resources per query

            JobInfo jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var mockSearchResult = new SearchResult(_mockedSearchCount, new List<Tuple<string, string>>());
            mockSearchResult.ReindexResult = new SearchResultReindex()
            {
                Count = _mockedSearchCount,
                StartResourceSurrogateId = startResourceSurrogateId,
                EndResourceSurrogateId = endResourceSurrogateId,
            };

            // Setup search result for count queries
            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_count") && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(mockSearchResult);

            var defaultSearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_count") && l.Any(t => t.Item1 == "_type" && t.Item2 != expectedResourceType)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(defaultSearchResult);

            // Setup search result for processing queries (non-count)
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
                Returns(CreateSearchResult(resourceType: resourceTypeHash));

            // Execute the orchestrator job
            var orchestratorTask = reindexOrchestratorJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);

            // Simulate processing job execution by running them in parallel
            _ = Task.Run(
                async () =>
                {
                    await Task.Delay(5000, _cancellationToken); // Give orchestrator time to create jobs

                    // Get all processing jobs created by the orchestrator
                    var processingJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
                    var childJobs = processingJobs.Where(j => j.Id != jobInfo.Id).ToList();

                    // Execute each processing job
                    foreach (var childJob in childJobs)
                    {
                        try
                        {
                            childJob.Status = JobStatus.Running;
                            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(childJob, _cancellationToken);
                            childJob.Status = JobStatus.Completed;
                            childJob.Result = result;
                            await _queueClient.CompleteJobAsync(childJob, false, _cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            childJob.Status = JobStatus.Failed;
                            childJob.Result = JsonConvert.SerializeObject(new { Error = ex.Message });
                            await _queueClient.CompleteJobAsync(childJob, false, _cancellationToken);
                        }
                    }
                },
                _cancellationToken);

            // Wait for orchestrator job to complete
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(await orchestratorTask);

            // Verify search for count was called
            await _searchService.Received().SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Is(true),
                Arg.Any<CancellationToken>(),
                true);

            // Verify specific search for Account resource type
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_count") && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true);

            var reindexJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, 1, true, _cancellationToken);
            var processingJob = reindexJobs.FirstOrDefault(j => j.Id != jobInfo.Id);

            Assert.NotNull(processingJob);
            Assert.Equal(JobStatus.Completed, processingJob.Status);

            var processingJobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(processingJob.Definition);
            Assert.Equal(_mockedSearchCount, processingJobDefinition.ResourceCount.Count);
            Assert.Equal(expectedResourceType, processingJobDefinition.ResourceType);
            Assert.Contains(param.Url.ToString(), processingJobDefinition.SearchParameterUrls);
        }

        [Fact]
        public async Task GivenNoSupportedParams_WhenExecuted_ThenJobCompletesWithNoWork()
        {
            var job = CreateReindexJobRecord();

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap.First().Value);

            JobInfo jobInfo = new JobInfo()
            {
                Id = 3,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 3,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };
            var result = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(await _reindexOrchestratorJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));
            Assert.Equal("Nothing to process. Reindex complete.", result.Error.First().Diagnostics);
            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, false, _cancellationToken);
            Assert.False(jobs.Any());
        }

        private SearchResult CreateSearchResult(string resourceType, string continuationToken = null, int resourceCount = 1)
        {
            var resultList = new List<SearchResultEntry>();

            for (var i = 0; i < resourceCount; i++)
            {
                var wrapper = Substitute.For<ResourceWrapper>();
                var propertyInfo = wrapper.GetType().GetProperty("ResourceTypeName");
                propertyInfo.SetValue(wrapper, resourceType);

                var entry = new SearchResultEntry(wrapper);

                resultList.Add(entry);
            }

            var searchResult = new SearchResult(resultList, continuationToken, null, new List<Tuple<string, string>>());
            searchResult.MaxResourceSurrogateId = 1;
            searchResult.TotalCount = resultList.Count;
            return searchResult;
        }

        private ReindexJobRecord CreateReindexJobRecord(uint maxResourcePerQuery = 100, IReadOnlyDictionary<string, string> paramHashMap = null)
        {
            if (paramHashMap == null)
            {
                paramHashMap = new Dictionary<string, string>() { { "Patient", "patientHash" } };
            }

            return new ReindexJobRecord(paramHashMap, new List<string>(), new List<string>(), new List<string>(), maxResourcePerQuery);
        }
    }
}
