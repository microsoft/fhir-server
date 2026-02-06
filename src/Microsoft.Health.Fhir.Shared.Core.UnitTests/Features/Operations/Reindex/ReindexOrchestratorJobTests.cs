// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat.Enforcers;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [CollectionDefinition("ReindexOrchestratorJobTests")]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexOrchestratorJobTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly ISearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterDefinitionManager _searchDefinitionManager;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private IQueueClient _queueClient;
        private readonly CancellationToken _cancellationToken;

        public ReindexOrchestratorJobTests()
        {
            // Configure cancellation token source with 1 minute timeout
            _cancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(1));
            _cancellationToken = _cancellationTokenSource.Token;

            _searchDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterStatusManager = Substitute.For<ISearchParameterStatusManager>();
            _searchParameterOperations = Substitute.For<ISearchParameterOperations>();

            // Initialize a fresh queue client for each test
            _queueClient = new TestQueueClient();
        }

        private void Dispose()
        {
            // Clean up the queue client if it's a TestQueueClient
            if (_queueClient is TestQueueClient testQueueClient)
            {
                testQueueClient.ClearJobs();
            }

            _cancellationTokenSource?.Dispose();
        }

        private ReindexOrchestratorJob CreateReindexOrchestratorJob(IFhirRuntimeConfiguration runtimeConfig = null)
        {
            runtimeConfig ??= new AzureHealthDataServicesRuntimeConfiguration();

            var coreFeatureConfig = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            coreFeatureConfig.Value.Returns(new CoreFeatureConfiguration());

            var operationsConfig = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfig.Value.Returns(new OperationsConfiguration());

            return new ReindexOrchestratorJob(
                _queueClient,
                () => _searchService.CreateMockScope(),
                _searchDefinitionManager,
                ModelInfoProvider.Instance,
                _searchParameterStatusManager,
                _searchParameterOperations,
                runtimeConfig,
                NullLoggerFactory.Instance,
                coreFeatureConfig,
                operationsConfig);
        }

        private async Task<JobInfo> CreateReindexJobRecord(
            uint maxResourcePerQuery = 100,
            IReadOnlyDictionary<string, string> paramHashMap = null,
            List<string> targetResourceTypes = null)
        {
            paramHashMap ??= new Dictionary<string, string> { { "Patient", "patientHash" } };
            targetResourceTypes ??= new List<string>();

            var jobRecord = new ReindexJobRecord(
                paramHashMap,
                targetResourceTypes,
                new List<string>(),
                new List<string>(),
                maxResourcePerQuery);

            // Enqueue the orchestrator job through the queue client
            var orchestratorJobs = await _queueClient.EnqueueAsync(
                (byte)QueueType.Reindex,
                new[] { JsonConvert.SerializeObject(jobRecord) },
                groupId: 0,
                forceOneActiveJobGroup: false,
                _cancellationToken);

            // Return the enqueued job with Running status
            var jobInfo = orchestratorJobs.First();
            jobInfo.Status = JobStatus.Running;

            return jobInfo;
        }

        private SearchResult CreateSearchResult(
            int resourceCount = 1,
            string continuationToken = null,
            long startSurrogateId = 1,
            long endSurrogateId = 1000,
            string resourceType = "Patient")
        {
            var resultList = new List<SearchResultEntry>();

            for (var i = 0; i < resourceCount; i++)
            {
                var wrapper = Substitute.For<ResourceWrapper>();
                var propertyInfo = wrapper.GetType().GetProperty("ResourceTypeName");
                propertyInfo?.SetValue(wrapper, resourceType);

                var entry = new SearchResultEntry(wrapper);
                resultList.Add(entry);
            }

            var searchResult = new SearchResult(resultList, continuationToken, null, new List<Tuple<string, string>>());
            searchResult.MaxResourceSurrogateId = endSurrogateId;
            searchResult.TotalCount = resourceCount;
            searchResult.ReindexResult = new SearchResultReindex
            {
                Count = resourceCount,
                StartResourceSurrogateId = startSurrogateId,
                EndResourceSurrogateId = endSurrogateId,
            };
            return searchResult;
        }

        private SearchParameterInfo CreateSearchParameterInfo(
            string url = "http://hl7.org/fhir/SearchParameter/Patient-name",
            List<string> baseResourceTypes = null,
            string resourceType = "Patient")
        {
            baseResourceTypes ??= new List<string> { resourceType };

            return new SearchParameterInfo(
                resourceType,
                "name",
                ValueSets.SearchParamType.String,
                url: new Uri(url),
                baseResourceTypes: baseResourceTypes)
            {
                IsSearchable = true,
                SearchParameterStatus = SearchParameterStatus.Supported,
            };
        }

        /// <summary>
        /// Sets up the GetSurrogateIdRanges mock to return ranges only on the first call (when startId is less than or equal to rangeEnd).
        /// This simulates the batched behavior where subsequent calls with advanced startId return empty.
        /// </summary>
        private void SetupGetSurrogateIdRangesMock(long rangeStart = 1, long rangeEnd = 10, string resourceType = null)
        {
            if (resourceType != null)
            {
                _searchService.GetSurrogateIdRanges(
                    resourceType,
                    Arg.Any<long>(),
                    Arg.Any<long>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                    .Returns(callInfo =>
                    {
                        var startId = callInfo.ArgAt<long>(1);
                        if (startId <= rangeEnd)
                        {
                            return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(
                                new List<(long StartId, long EndId, int Count)> { (rangeStart, rangeEnd, 1) });
                        }

                        return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(
                            new List<(long StartId, long EndId, int Count)>());
                    });
            }
            else
            {
                _searchService.GetSurrogateIdRanges(
                    Arg.Any<string>(),
                    Arg.Any<long>(),
                    Arg.Any<long>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                    .Returns(callInfo =>
                    {
                        var startId = callInfo.ArgAt<long>(1);
                        if (startId <= rangeEnd)
                        {
                            return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(
                                new List<(long StartId, long EndId, int Count)> { (rangeStart, rangeEnd, 1) });
                        }

                        return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(
                            new List<(long StartId, long EndId, int Count)>());
                    });
            }
        }

        private async Task<List<JobInfo>> WaitForJobsAsync(long groupId, TimeSpan timeout, int expectedMinimumJobs = 1)
        {
            var startTime = DateTime.UtcNow;
            var pollInterval = TimeSpan.FromMilliseconds(100);

            while (DateTime.UtcNow - startTime < timeout)
            {
                var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, groupId, true, _cancellationToken);
                var processingJobs = jobs.Where(j => j.Id != groupId).ToList();

                if (processingJobs.Count >= expectedMinimumJobs)
                {
                    return processingJobs;
                }

                await Task.Delay(pollInterval, _cancellationToken);
            }

            // Return whatever jobs we have after timeout
            var finalJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, groupId, true, _cancellationToken);
            return finalJobs.Where(j => j.Id != groupId).ToList();
        }

        [Fact]
        public async Task ExecuteAsync_WhenCancellationRequested_ReturnsJobCancelledResult()
        {
            // Arrange
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(10); // Cancel after short delay

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, cancellationTokenSource.Token);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.NotNull(jobResult.Error);
            Assert.Contains(jobResult.Error, e => e.Diagnostics.Contains("cancelled"));
        }

        [Fact]
        public async Task ExecuteAsync_WithExceptionInProcessing_ReturnsErrorResult()
        {
            // Arrange
            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(Task.FromException<IReadOnlyCollection<ResourceSearchParameterStatus>>(
                    new InvalidOperationException("Database error")));

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.NotNull(jobResult.Error);
            Assert.Contains(jobResult.Error, e => e.Severity == OperationOutcomeConstants.IssueSeverity.Error);
        }

        [Fact]
        public async Task AddErrorResult_AddsErrorToExistingErrors()
        {
            // Arrange
            var initialError = new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Information,
                OperationOutcomeConstants.IssueType.Informational,
                "Initial error");

            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var jobInfo = await CreateReindexJobRecord();

            jobInfo.Result = JsonConvert.SerializeObject(new ReindexOrchestratorJobResult
            {
                Error = new List<OperationOutcomeIssue> { initialError },
            });

            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.True(jobResult.Error.Count >= 2, "Should have at least 2 errors (initial + new)");
        }

        [Fact]
        public async Task GetDerivedResourceTypes_WithResourceBaseType_ReturnsAllResourceTypes()
        {
            // Arrange - Create a search parameter that applies to the base Resource type
            var searchParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Resource-id",
                baseResourceTypes: new List<string> { KnownResourceTypes.Resource },
                resourceType: KnownResourceTypes.Resource);

            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Get all resource types from the model info provider
            var allResourceTypes = ModelInfoProvider.Instance.GetResourceTypeNames().ToList();

            // Set up search results to return data for all resource types
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(x =>
                {
                    // Parse the resource type from the query parameters
                    var queryParams = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(0);
                    var resourceTypeParam = queryParams.FirstOrDefault(p => p.Item1 == KnownQueryParameterNames.Type);
                    var resourceType = resourceTypeParam?.Item2 ?? "Patient";

                    return CreateSearchResult(
                        resourceCount: 10,
                        resourceType: resourceType);
                });

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 10);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created - expect jobs for all resource types
            // Use longer timeout and wait for all types since dictionary iteration order is non-deterministic
            var processingJobs = await WaitForJobsAsync(
                jobInfo.GroupId,
                TimeSpan.FromSeconds(120),
                expectedMinimumJobs: allResourceTypes.Count); // Wait for all resource types

            // Assert
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created");

            // Get unique resource types from the processing jobs
            var resourceTypesInJobs = processingJobs
                .Select(j => JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition))
                .Select(def => def.ResourceType)
                .Distinct()
                .ToList();

            // Verify that a significant number of resource types were included
            // Since Resource is the base type, we should have jobs for MANY resource types
            Assert.True(
                resourceTypesInJobs.Count >= 10,
                $"Should have jobs for at least 10 different resource types when base type is Resource. Found {resourceTypesInJobs.Count}: {string.Join(", ", resourceTypesInJobs)}");

            // Verify that both DomainResource types AND non-DomainResource types are included
            Assert.Contains("Patient", resourceTypesInJobs); // DomainResource descendant
            Assert.Contains("Observation", resourceTypesInJobs); // DomainResource descendant

            // These should be included since Resource is the base type (unlike DomainResource which excludes them)
            var hasAnyNonDomainResource = resourceTypesInJobs.Any(rt =>
                rt == "Binary" || rt == "Bundle" || rt == "Parameters");

            Assert.True(
                hasAnyNonDomainResource,
                $"When base type is Resource, should include non-DomainResource types (Binary, Bundle, Parameters). Found: {string.Join(", ", resourceTypesInJobs)}");

            // Log the actual resource types found
            var expectedResourceTypes = allResourceTypes.Count;
            var foundResourceTypes = resourceTypesInJobs.Count;
        }

        [Fact]
        public async Task GetDerivedResourceTypes_WithDomainResourceBaseType_ReturnsApplicableResourceTypes()
        {
            // Arrange - Create a search parameter that applies to DomainResource
            var searchParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/DomainResource-text",
                baseResourceTypes: new List<string> { KnownResourceTypes.DomainResource },
                resourceType: KnownResourceTypes.DomainResource);

            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            // Mock SearchParameterHashMap for RefreshSearchParameterCache
            var paramHashMap = new Dictionary<string, string> { { "Patient", "patientHash" } };
            _searchDefinitionManager.SearchParameterHashMap
                .Returns(paramHashMap);

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Set up search results for various resource types
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(x =>
                {
                    // Parse the resource type from the query parameters
                    var queryParams = x.ArgAt<IReadOnlyList<Tuple<string, string>>>(0);
                    var resourceTypeParam = queryParams.FirstOrDefault(p => p.Item1 == KnownQueryParameterNames.Type);
                    var resourceType = resourceTypeParam?.Item2 ?? "Patient";

                    // Return count based on whether it's a DomainResource type
                    var isDomainResourceType = resourceType != "Binary" &&
                                                resourceType != "Bundle" &&
                                                resourceType != "Parameters";

                    return CreateSearchResult(
                        resourceCount: isDomainResourceType ? 10 : 0,
                        resourceType: resourceType);
                });

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 10);

            // Get the expected DomainResource types (excluding Binary, Bundle, Parameters)
            var allResourceTypes = ModelInfoProvider.Instance.GetResourceTypeNames().ToList();
            var domainResourceTypes = allResourceTypes.Where(rt =>
                rt != "Binary" && rt != "Bundle" && rt != "Parameters").ToList();

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created - wait for all DomainResource types
            // Use longer timeout since dictionary iteration order is non-deterministic
            var processingJobs = await WaitForJobsAsync(
                jobInfo.GroupId,
                TimeSpan.FromSeconds(120),
                expectedMinimumJobs: domainResourceTypes.Count);

            // Assert
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created");

            // Get unique resource types from the processing jobs
            var resourceTypesInJobs = processingJobs
                .Select(j => JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition))
                .Select(def => def.ResourceType)
                .Distinct()
                .ToList();

            // Verify that many DomainResource types were included
            Assert.True(resourceTypesInJobs.Count >= domainResourceTypes.Count - 5, $"Should have jobs for most resource types derived from DomainResource. Found: {resourceTypesInJobs.Count}, Expected at least: {domainResourceTypes.Count - 5}");

            // Verify that non-DomainResource types were excluded
            Assert.DoesNotContain("Binary", resourceTypesInJobs);
            Assert.DoesNotContain("Bundle", resourceTypesInJobs);
            Assert.DoesNotContain("Parameters", resourceTypesInJobs);
        }

        [Fact]
        public async Task GetResourceCountForQueryAsync_WithValidQuery_ReturnsSearchResult()
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo();
            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchDefinitionManager.GetSearchParameter(searchParam.Url.OriginalString)
                .Returns(searchParam);

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Mock SearchParameterHashMap for RefreshSearchParameterCache
            // IMPORTANT: Return a concrete Dictionary instance, not a mock
            var paramHashMap = new Dictionary<string, string>
            {
                { "Patient", "patientHash" },
                { "Observation", "observationHash" },
                { "Condition", "conditionHash" },
            };

            // Return the concrete dictionary as IReadOnlyDictionary
            _searchDefinitionManager.SearchParameterHashMap
                .Returns(paramHashMap);

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
            .Returns("hash");

            // Create a search result with 100 resources
            var searchResult = CreateSearchResult(
                resourceCount: 100,
                startSurrogateId: 1,
                endSurrogateId: 100,
                resourceType: "Patient");

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 100, resourceType: "Patient");

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);

            // Assert - Verify that SearchForReindexAsync was called
            await _searchService.Received().SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>());

            // Assert - Verify processing jobs were created
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created based on search results");

            // Assert - Verify the job definitions contain the correct resource counts
            var firstJob = processingJobs.First();
            var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(firstJob.Definition);

            Assert.NotNull(jobDef);
            Assert.Equal("Patient", jobDef.ResourceType);
            Assert.NotNull(jobDef.ResourceCount);
            Assert.True(jobDef.ResourceCount.Count > 0, "Resource count should be greater than 0");
            Assert.Equal(1, jobDef.ResourceCount.StartResourceSurrogateId);
            Assert.Equal(100, jobDef.ResourceCount.EndResourceSurrogateId);

            // Assert - Verify search parameter hash is set
            Assert.NotNull(jobDef.ResourceTypeSearchParameterHashMap);
            Assert.Equal("patientHash", jobDef.ResourceTypeSearchParameterHashMap);

            // Assert - Verify search parameter URLs are included
            Assert.NotNull(jobDef.SearchParameterUrls);
            Assert.Contains(searchParam.Url.OriginalString, jobDef.SearchParameterUrls);
        }

        [Fact]
        public async Task EnqueueQueryProcessingJobsAsync_WithValidSearchParameters_CreatesProcessingJobs()
        {
            var searchParam = CreateSearchParameterInfo();
            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchDefinitionManager.GetSearchParameter(searchParam.Url.OriginalString)
                .Returns(searchParam);

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResultWithData = CreateSearchResult(resourceCount: 250);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResultWithData);

            var ranges = new List<(long, long, int)>
            {
                (1, 100, 100),
                (101, 200, 100),
                (201, 250, 50),
            };
            _searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var startId = callInfo.ArgAt<long>(1);
                    if (startId <= 1)
                    {
                        return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(ranges);
                    }

                    return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(
                        new List<(long StartId, long EndId, int Count)>());
                });

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob(new AzureHealthDataServicesRuntimeConfiguration());

            // Act: Fire off execute asynchronously without awaiting
            var executeTask = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Assert: Check that processing jobs were created
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been enqueued");

            // Cancel the orchestrator job to stop it from waiting for completion
            await _queueClient.CancelJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, _cancellationToken);

            // Now safely await the result
            var result = await executeTask;
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task UpdateSearchParameterStatus_WithPendingDisableStatus_UpdatesToDisabled()
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo();
            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.PendingDisable,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                Arg.Is<List<string>>(l => l.Contains(searchParam.Url.ToString())),
                SearchParameterStatus.Disabled,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenNoSupportedParams_WhenExecuted_ThenJobCompletesWithNoWork()
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo();
            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.PendingDelete,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                Arg.Is<List<string>>(l => l.Contains(searchParam.Url.ToString())),
                SearchParameterStatus.Deleted,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task UpdateSearchParameterStatus_WithNullReadySearchParameters_ReturnsEarly()
        {
            // Arrange
            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            await _searchParameterStatusManager.DidNotReceive().UpdateSearchParameterStatusAsync(
                Arg.Any<List<string>>(),
                Arg.Any<SearchParameterStatus>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GetValidSearchParameterUrlsForResourceType_WithValidParameters_ReturnsFilteredUrls()
        {
            // Arrange
            var patientNameParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-name",
                baseResourceTypes: new List<string> { "Patient" },
                resourceType: "Patient");

            var patientBirthdateParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
                baseResourceTypes: new List<string> { "Patient" },
                resourceType: "Patient");

            // Create a third parameter that should be filtered OUT
            // (not valid for Patient even though it's in the reindex job)
            var observationCodeParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Observation-code",
                baseResourceTypes: new List<string> { "Observation" },
                resourceType: "Observation");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientNameParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientBirthdateParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus3 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(observationCodeParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2, searchParamStatus3 });

            // ALL three parameters are in the reindex job
            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { patientNameParam, patientBirthdateParam, observationCodeParam });

            // GetSearchParameters("Patient") returns ONLY the Patient parameters
            // This is the first level of filtering
            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { patientNameParam, patientBirthdateParam });

            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/SearchParameter/Patient-name" => patientNameParam,
                    "http://hl7.org/fhir/SearchParameter/Patient-birthdate" => patientBirthdateParam,
                    "http://hl7.org/fhir/SearchParameter/Observation-code" => observationCodeParam,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResult = CreateSearchResult(resourceCount: 10);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 10, resourceType: "Patient");

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);

            // Assert - Verify jobs were created
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created");

            var firstJob = processingJobs.First();
            var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(firstJob.Definition);

            // Assert - Verify SearchParameterUrls exist and are filtered correctly
            Assert.NotNull(jobDef.SearchParameterUrls);

            // Assert - Verify BOTH Patient parameters are included
            Assert.Contains(patientNameParam.Url.OriginalString, jobDef.SearchParameterUrls);
            Assert.Contains(patientBirthdateParam.Url.OriginalString, jobDef.SearchParameterUrls);

            // Assert - Verify Observation parameter is EXCLUDED (this is the key filtering behavior!)
            Assert.DoesNotContain(observationCodeParam.Url.OriginalString, jobDef.SearchParameterUrls);

            // Assert - Verify exactly 2 parameters (the two Patient parameters)
            Assert.Equal(2, jobDef.SearchParameterUrls.Count);

            // Assert - Verify the job is for Patient resource type
            Assert.Equal("Patient", jobDef.ResourceType);
        }

        [Fact]
        public async Task GetValidSearchParameterUrlsForResourceType_WithExceptionInValidation_UsesFallback()
        {
            // Arrange
            var searchParam1 = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-name",
                resourceType: "Patient");

            var searchParam2 = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
                resourceType: "Patient");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam1.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam2.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam1, searchParam2 });

            // Simulate an exception when trying to get search parameters for Patient
            // This triggers the fallback behavior
            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(x => throw new Exception("Validation error"));

            // The fallback should still allow GetSearchParameter to work for individual URLs
            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/SearchParameter/Patient-name" => searchParam1,
                    "http://hl7.org/fhir/SearchParameter/Patient-birthdate" => searchParam2,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResult = CreateSearchResult(resourceCount: 10);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 10, resourceType: "Patient");

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);

            // Assert - Verify that jobs were still created despite the exception
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created even when validation throws exception");

            // Assert - Verify that the fallback behavior was used
            // The fallback returns ALL search parameters from _reindexJobRecord.SearchParams
            var firstJob = processingJobs.First();
            var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(firstJob.Definition);

            Assert.NotNull(jobDef.SearchParameterUrls);

            // The fallback should include ALL search parameters from the reindex job
            // Since the exception occurred, it should use _reindexJobRecord.SearchParams.ToList()
            Assert.Contains(searchParam1.Url.OriginalString, jobDef.SearchParameterUrls);
            Assert.Contains(searchParam2.Url.OriginalString, jobDef.SearchParameterUrls);

            // Verify that both parameters are present (the fallback includes all)
            Assert.Equal(2, jobDef.SearchParameterUrls.Count);
        }

        [Fact]
        public async Task CheckJobRecordForAnyWork_WithZeroResources_ReturnsErrorIndicatingNoWork()
        {
            // Arrange
            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var jobInfo = await CreateReindexJobRecord();

            // Modify the job record to have zero resources
            var jobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
            jobRecord.Count = 0;
            jobRecord.ResourceCounts.Clear();
            jobInfo.Definition = JsonConvert.SerializeObject(jobRecord);

            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert - Verify the observable behavior when CheckJobRecordForAnyWork returns false
            Assert.NotNull(jobResult);
            Assert.NotNull(jobResult.Error);
            Assert.True(jobResult.Error.Count > 0, "Should have at least one error");

            // Verify the error indicates no work to process - checking multiple possible error messages
            var hasNoWorkError = jobResult.Error.Any(e =>
                e.Diagnostics != null && e.Diagnostics.Contains("Nothing to process", StringComparison.OrdinalIgnoreCase));

            Assert.True(hasNoWorkError, $"Expected error indicating no resources to reindex. Actual errors: {string.Join(", ", jobResult.Error.Select(e => e.Diagnostics))}");
        }

        [Fact]
        public async Task ProcessCompletedJobs_WithFailedJobs_HandlesFailed()
        {
            // Arrange - Create multiple search parameters
            var patientNameParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-name",
                resourceType: "Patient");

            var patientBirthdateParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
                resourceType: "Patient");

            var observationCodeParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Observation-code",
                baseResourceTypes: new List<string> { "Observation" },
                resourceType: "Observation");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientNameParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientBirthdateParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus3 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(observationCodeParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2, searchParamStatus3 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { patientNameParam, patientBirthdateParam, observationCodeParam });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { patientNameParam, patientBirthdateParam });

            _searchDefinitionManager.GetSearchParameters("Observation")
                .Returns(new List<SearchParameterInfo> { observationCodeParam });

            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/SearchParameter/Patient-name" => patientNameParam,
                    "http://hl7.org/fhir/SearchParameter/Patient-birthdate" => patientBirthdateParam,
                    "http://hl7.org/fhir/SearchParameter/Observation-code" => observationCodeParam,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Return different results for different resource types
            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item2 == "Patient")),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(CreateSearchResult(resourceCount: 100, resourceType: "Patient"));

            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item2 == "Observation")),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(CreateSearchResult(resourceCount: 50, resourceType: "Observation"));

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 100, resourceType: "Patient");
            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 50, resourceType: "Observation");

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act - Start the orchestrator
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 2);

            // Assert - Verify jobs were created
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created");

            // **SIMULATE JOB FAILURE** - Find and fail Patient jobs specifically
            var patientJobs = processingJobs
                .Where(j =>
                {
                    try
                    {
                        var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition);
                        return jobDef.ResourceType == "Patient";
                    }
                    catch (JsonException)
                    {
                        // Skip jobs that can't be deserialized (e.g., orchestrator job)
                        return false;
                    }
                })
                .ToList();

            Assert.True(patientJobs.Count > 0, "Should have at least one Patient processing job to fail");

            var jobToFail = patientJobs.First();

            var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobToFail.Definition);

            // Create a failed result
            var failedResult = new ReindexProcessingJobResult
            {
                SucceededResourceCount = 0,
                FailedResourceCount = (int)jobDef.ResourceCount.Count,
            };

            jobToFail.Status = JobStatus.Failed;
            jobToFail.Result = JsonConvert.SerializeObject(failedResult);

            // Update the job in the queue to mark it as failed
            await _queueClient.CompleteJobAsync(jobToFail, false, _cancellationToken);

            // Wait a bit for the orchestrator to process the failed jobs
            await Task.Delay(TimeSpan.FromSeconds(2), _cancellationToken);

            // Get the updated jobs
            var updatedJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
            var failedJobs = updatedJobs.Where(j => j.Status == JobStatus.Failed && j.Id != jobInfo.Id).ToList();
            var succeededJobs = updatedJobs.Where(j => j.Status == JobStatus.Completed && j.Id != jobInfo.Id).ToList();

            // Assert - Verify failed jobs exist
            Assert.True(failedJobs.Count > 0, "Should have at least one failed job");

            // Assert - Verify the failed job is the one we failed
            var failedJobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(failedJobs.First().Definition);
            Assert.Equal("Patient", failedJobDef.ResourceType);

            // Assert - Verify search parameters from failed jobs should NOT be marked as ready
            // The orchestrator's ProcessCompletedJobs should:
            // 1. Remove Patient search parameters from the ready list (because Patient job failed)
            // 2. Keep Observation search parameters in the ready list (if Observation jobs succeeded)

            // Verify that Patient search parameters were NOT updated to Enabled
            await _searchParameterStatusManager.DidNotReceive().UpdateSearchParameterStatusAsync(
                Arg.Is<List<string>>(l => l.Contains(patientNameParam.Url.ToString())),
                SearchParameterStatus.Enabled,
                Arg.Any<CancellationToken>());

            await _searchParameterStatusManager.DidNotReceive().UpdateSearchParameterStatusAsync(
                Arg.Is<List<string>>(l => l.Contains(patientBirthdateParam.Url.ToString())),
                SearchParameterStatus.Enabled,
                Arg.Any<CancellationToken>());

            // If we have succeeded Observation jobs, those parameters should be updated
            if (succeededJobs.Any(j =>
            {
                var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition);
                return def.ResourceType == "Observation";
            }))
            {
                await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                    Arg.Is<List<string>>(l => l.Contains(observationCodeParam.Url.ToString())),
                    SearchParameterStatus.Enabled,
                    Arg.Any<CancellationToken>());
            }

            // Assert - Verify the orchestrator logged the failures
            // The result should contain error information about failed resources
            var orchestratorJob = updatedJobs.FirstOrDefault(j => j.Id == jobInfo.Id);
            if (orchestratorJob?.Result != null)
            {
                var orchestratorResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(orchestratorJob.Result);

                // Failed jobs should be counted in FailedResources
                Assert.True(orchestratorResult.FailedResources > 0, $"Expected FailedResources > 0, but got {orchestratorResult.FailedResources}");

                // Error information should be present
                Assert.NotNull(orchestratorResult.Error);
                Assert.True(orchestratorResult.Error.Count > 0, "Should have error information about failed jobs");
            }
        }

        [Fact]
        public async Task CalculateTotalCount_WithMultipleResourceTypes_CountsAllResources()
        {
            // Create search parameters for multiple resource types
            var patientParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-name",
                resourceType: "Patient");

            var observationParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Observation-code",
                baseResourceTypes: new List<string> { "Observation" },
                resourceType: "Observation");

            var conditionParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Condition-code",
                baseResourceTypes: new List<string> { "Condition" },
                resourceType: "Condition");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(observationParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus3 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(conditionParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2, searchParamStatus3 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { patientParam, observationParam, conditionParam });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Return different counts for different resource types
            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l =>
                    l.Any(t => t.Item2 == "Patient")),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(CreateSearchResult(resourceCount: 30, resourceType: "Patient"));

            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l =>
                    l.Any(t => t.Item2 == "Observation")),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(CreateSearchResult(resourceCount: 25, resourceType: "Observation"));

            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l =>
                    l.Any(t => t.Item2 == "Condition")),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(CreateSearchResult(resourceCount: 20, resourceType: "Condition"));

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 30, resourceType: "Patient");
            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 25, resourceType: "Observation");
            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 20, resourceType: "Condition");

            // Arrange
            var jobRecord = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobRecord, _cancellationToken);

            // Assert - Verify the jobs created and their ResourceCount.Count
            var processingJobs = await WaitForJobsAsync(jobRecord.GroupId, TimeSpan.FromSeconds(60), expectedMinimumJobs: 3);

            // Verify processing jobs were created
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created");

            // Group jobs by resource type and verify counts
            var jobsByResourceType = processingJobs
                .Select(j => JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(j.Definition))
                .GroupBy(def => def.ResourceType)
                .ToDictionary(g => g.Key, g => g.Sum(def => (int)def.ResourceCount.Count));

            // Verify we have jobs for all three resource types
            Assert.True(jobsByResourceType.ContainsKey("Patient"), "Should have Patient jobs");
            Assert.True(jobsByResourceType.ContainsKey("Observation"), "Should have Observation jobs");
            Assert.True(jobsByResourceType.ContainsKey("Condition"), "Should have Condition jobs");

            // Verify each resource type has the correct total count
            Assert.Equal(30, jobsByResourceType["Patient"]);
            Assert.Equal(25, jobsByResourceType["Observation"]);
            Assert.Equal(20, jobsByResourceType["Condition"]);

            // Verify the total matches the sum of all resource types
            var totalFromJobs = jobsByResourceType.Values.Sum();
            Assert.Equal(75, totalFromJobs);
        }

        [Fact]
        public async Task ProcessCompletedJobsAndDetermineReadiness_WithReadyParameters_ReturnsReadyList()
        {
            // Arrange - Create search parameters for Patient
            var patientNameParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-name",
                resourceType: "Patient");

            var patientBirthdateParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-birthdate",
                resourceType: "Patient");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientNameParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientBirthdateParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { patientNameParam, patientBirthdateParam });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { patientNameParam, patientBirthdateParam });

            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/SearchParameter/Patient-name" => patientNameParam,
                    "http://hl7.org/fhir/SearchParameter/Patient-birthdate" => patientBirthdateParam,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Track whether jobs have been marked complete to change mock behavior
            bool jobsCompleted = false;

            // Create search results with actual resources to trigger job creation
            // BUT after jobs complete, return zero resources to simulate successful reindexing
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    // If jobs have been completed, return zero resources (successful reindex)
                    if (jobsCompleted)
                    {
                        return new SearchResult(0, new List<Tuple<string, string>>());
                    }

                    // Otherwise, return resources that need reindexing
                    return CreateSearchResult(resourceCount: 100, resourceType: "Patient");
                });

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 100, resourceType: "Patient");

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act - Start the orchestrator
            var executeTask = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for processing jobs to be created
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);
            Assert.True(processingJobs.Count > 0, "Processing jobs should have been created");

            // Verify the created jobs have SearchParameterUrls in their definitions
            foreach (var job in processingJobs)
            {
                var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                Assert.NotNull(jobDef.SearchParameterUrls);
                Assert.Contains(patientNameParam.Url.ToString(), jobDef.SearchParameterUrls);
                Assert.Contains(patientBirthdateParam.Url.ToString(), jobDef.SearchParameterUrls);
            }

            // Mark all jobs as completed to simulate successful reindexing
            foreach (var job in processingJobs)
            {
                var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);

                var succeededResult = new ReindexProcessingJobResult
                {
                    SucceededResourceCount = (int)jobDef.ResourceCount.Count,
                    FailedResourceCount = 0,
                    SearchParameterUrls = jobDef.SearchParameterUrls,
                };

                job.Status = JobStatus.Completed;
                job.Result = JsonConvert.SerializeObject(succeededResult);

                await _queueClient.CompleteJobAsync(job, false, _cancellationToken);
            }

            // Now that jobs are completed, change mock to return zero resources
            // This simulates that reindexing was successful and no resources remain
            jobsCompleted = true;

            // Assert - Verify search parameters were marked as ready (updated to Enabled)
            // Retry every 1 second for up to 15 seconds until the method is called
            var retryCount = 0;
            var maxRetries = 15;
            var retryInterval = TimeSpan.FromSeconds(1);
            var receivedCall = false;

            while (retryCount < maxRetries && !receivedCall)
            {
                try
                {
                    await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                        Arg.Is<List<string>>(l =>
                            l.Contains(patientNameParam.Url.ToString()) ||
                            l.Contains(patientBirthdateParam.Url.ToString())),
                        SearchParameterStatus.Enabled,
                        Arg.Any<CancellationToken>());

                    receivedCall = true;
                }
                catch (NSubstitute.Exceptions.ReceivedCallsException)
                {
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(retryInterval, _cancellationToken);
                    }
                }
            }

            Assert.True(receivedCall, $"UpdateSearchParameterStatusAsync was not called after {maxRetries} retries over {maxRetries} seconds. The orchestrator may not have processed the completed jobs yet.");

            // Wait for execution to complete
            try
            {
                await executeTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Assert - Verify all jobs completed successfully
            var allJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
            var completedJobs = allJobs.Where(j => j.Status == JobStatus.Completed && j.Id != jobInfo.Id).ToList();

            Assert.True(completedJobs.Count > 0, "Should have completed jobs");
            Assert.All(completedJobs, job =>
            {
                var def = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(job.Result);

                Assert.True(result.SucceededResourceCount > 0, "Job should have succeeded resources");
                Assert.Equal(0, result.FailedResourceCount);
                Assert.NotNull(result.SearchParameterUrls);
            });

            // Assert - Verify orchestrator result shows success
            var orchestratorJob = allJobs.FirstOrDefault(j => j.Id == jobInfo.Id);
            if (orchestratorJob?.Result != null)
            {
                var orchestratorResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(orchestratorJob.Result);

                Assert.True(orchestratorResult.SucceededResources > 0, "Should have succeeded resources");
                Assert.Equal(0, orchestratorResult.FailedResources);
            }
        }

        [Fact]
        public async Task HandleException_LogsAndReturnsError()
        {
            // Arrange
            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(Task.FromException<IReadOnlyCollection<ResourceSearchParameterStatus>>(
                    new SystemException("Critical error")));

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.NotNull(jobResult.Error);
            Assert.True(jobResult.Error.Count > 0);
            Assert.Contains(jobResult.Error, e => e.Severity == OperationOutcomeConstants.IssueSeverity.Error);
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithTargetResourceTypes_FiltersCorrectly()
        {
            // Arrange
            var targetResourceTypes = new List<string> { "Patient" };

            var patientParam = CreateSearchParameterInfo(resourceType: "Patient");
            var observationParam = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Observation-code",
                resourceType: "Observation");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(patientParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(observationParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { patientParam, observationParam });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { patientParam });

            _searchDefinitionManager.GetSearchParameter(patientParam.Url.OriginalString)
                .Returns(patientParam);

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResultWithData = CreateSearchResult(resourceCount: 250);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResultWithData);

            var ranges = new List<(long, long, int)>
            {
                (1, 100, 100),
                (101, 200, 100),
                (201, 250, 50),
            };
            _searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    var startId = callInfo.ArgAt<long>(1);
                    if (startId <= 1)
                    {
                        return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(ranges);
                    }

                    return Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(
                        new List<(long StartId, long EndId, int Count)>());
                });

            var jobInfo = await CreateReindexJobRecord(targetResourceTypes: targetResourceTypes);
            var orchestrator = CreateReindexOrchestratorJob(new AzureHealthDataServicesRuntimeConfiguration());

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);

            foreach (var job in processingJobs)
            {
                var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                Assert.Equal("Patient", jobDef.ResourceType);
            }
        }

        [Fact]
        public async Task CreateReindexOrchestratorJobAsync_WithResourceTypesWithZeroCount_MarksParametersAsEnabled()
        {
            // Arrange
            var searchParam = CreateSearchParameterInfo();
            var searchParamStatus = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            // Return zero count for the search result
            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);

            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                Arg.Any<List<string>>(),
                SearchParameterStatus.Enabled,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithCaseVariantSearchParameterUrls_ReturnsBothEntries()
        {
            // Arrange - Test case where we have case-variant SearchParameter URLs with same status
            // Create case-variant URLs (both should be treated separately with potential duplicates)
            var searchParamLowercase = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/searchparameter/patient-name",
                resourceType: "Patient");

            var searchParamMixedCase = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-name",
                resourceType: "Patient");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParamLowercase.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParamMixedCase.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParamLowercase, searchParamMixedCase });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { searchParamLowercase, searchParamMixedCase });

            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/searchparameter/patient-name" => searchParamLowercase,
                    "http://hl7.org/fhir/SearchParameter/Patient-name" => searchParamMixedCase,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResult = CreateSearchResult(resourceCount: 10);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            SetupGetSurrogateIdRangesMock(rangeStart: 1, rangeEnd: 10, resourceType: "Patient");

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            _ = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            var processingJobs = await WaitForJobsAsync(jobInfo.GroupId, TimeSpan.FromSeconds(30), expectedMinimumJobs: 1);

            Assert.True(processingJobs.Count > 0, "Should have created at least one processing job");

            var firstJob = processingJobs.First();
            var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(firstJob.Definition);

            Assert.NotNull(jobDef.SearchParameterUrls);
            Assert.Contains(searchParamLowercase.Url.OriginalString, jobDef.SearchParameterUrls);
            Assert.Contains(searchParamMixedCase.Url.OriginalString, jobDef.SearchParameterUrls);

            // Verify both case variants are present
            Assert.Equal(2, jobDef.SearchParameterUrls.Count(url =>
                url.Equals(searchParamLowercase.Url.OriginalString, StringComparison.Ordinal) ||
                url.Equals(searchParamMixedCase.Url.OriginalString, StringComparison.Ordinal)));
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithCaseVariantDifferentStatuses_ReturnsBothEntriesWithRespectiveStatuses()
        {
            // Arrange - Test case where we have case variant URLs with different statuses (Supported and PendingDelete)
            // Create case-variant URLs with different statuses
            var searchParamLowercase = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/searchparameter/patient-identifier",
                resourceType: "Patient");

            var searchParamMixedCase = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-Identifier",
                resourceType: "Patient");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParamLowercase.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParamMixedCase.Url.OriginalString),
                Status = SearchParameterStatus.PendingDelete,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParamLowercase, searchParamMixedCase });

            _searchDefinitionManager.GetSearchParameters("Patient")
                .Returns(new List<SearchParameterInfo> { searchParamLowercase, searchParamMixedCase });

            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/searchparameter/patient-identifier" => searchParamLowercase,
                    "http://hl7.org/fhir/SearchParameter/Patient-Identifier" => searchParamMixedCase,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResult = CreateSearchResult(resourceCount: 0);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                        Arg.Is<List<string>>(l => l.Contains(searchParamLowercase.Url.ToString())),
                        SearchParameterStatus.Enabled,
                        Arg.Any<CancellationToken>());

            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                        Arg.Is<List<string>>(l => l.Contains(searchParamMixedCase.Url.ToString())),
                        SearchParameterStatus.Deleted,
                        Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithMultipleCaseVariantsAndPendingDisable_MaintainsAllVariants()
        {
            // Arrange - Test with PendingDisable status
            var searchParam1 = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/searchparameter/patient-birthdate",
                resourceType: "Patient");

            var searchParam2 = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Patient-BirthDate",
                resourceType: "Patient");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam1.Url.OriginalString),
                Status = SearchParameterStatus.PendingDisable,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam2.Url.OriginalString),
                Status = SearchParameterStatus.PendingDisable,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam1, searchParam2 });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                Arg.Is<List<string>>(l => l.Contains(searchParam1.Url.ToString())),
                SearchParameterStatus.Disabled,
                Arg.Any<CancellationToken>());

            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                    Arg.Is<List<string>>(l => l.Contains(searchParam2.Url.ToString())),
                    SearchParameterStatus.Disabled,
                    Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CheckForCompletionAsync_WithCaseVariantUrls_MaintainsBothVariants()
        {
            // Arrange - Simulate completion checking with case variant URLs
            var searchParam1 = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/searchparameter/observation-code",
                resourceType: "Observation");

            var searchParam2 = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Observation-Code",
                resourceType: "Observation");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam1.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParam2.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParam1, searchParam2 });

            _searchDefinitionManager.GetSearchParameters("Observation")
                .Returns(new List<SearchParameterInfo> { searchParam1, searchParam2 });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResult = CreateSearchResult(resourceCount: 100);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var jobInfo = await CreateReindexJobRecord();
            var orchestrator = CreateReindexOrchestratorJob();

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }
    }
}
