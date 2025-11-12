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
        private readonly IQueueClient _queueClient = new TestQueueClient();
        private readonly CancellationToken _cancellationToken;

        public ReindexOrchestratorJobTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            _searchDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterStatusManager = Substitute.For<ISearchParameterStatusManager>();
            _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        }

        private ReindexOrchestratorJob CreateReindexOrchestratorJob(
            IFhirRuntimeConfiguration runtimeConfig = null,
            int searchParameterCacheRefreshIntervalSeconds = 1,
            int reindexDelayMultiplier = 1)
        {
            runtimeConfig ??= new AzureHealthDataServicesRuntimeConfiguration();

            var coreFeatureConfig = Substitute.For<IOptions<CoreFeatureConfiguration>>();
            coreFeatureConfig.Value.Returns(new CoreFeatureConfiguration
            {
                SearchParameterCacheRefreshIntervalSeconds = searchParameterCacheRefreshIntervalSeconds,
            });

            var operationsConfig = Substitute.For<IOptions<OperationsConfiguration>>();
            operationsConfig.Value.Returns(new OperationsConfiguration
            {
                Reindex = new ReindexJobConfiguration
                {
                    ReindexDelayMultiplier = reindexDelayMultiplier,
                },
            });

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

        private ReindexJobRecord CreateReindexJobRecord(
            uint maxResourcePerQuery = 100,
            IReadOnlyDictionary<string, string> paramHashMap = null,
            List<string> targetResourceTypes = null)
        {
            paramHashMap ??= new Dictionary<string, string> { { "Patient", "patientHash" } };
            targetResourceTypes ??= new List<string>();

            return new ReindexJobRecord(
                paramHashMap,
                targetResourceTypes,
                new List<string>(),
                new List<string>(),
                maxResourcePerQuery);
        }

        private SearchResult CreateSearchResult(
            int resourceCount = 1,
            string continuationToken = null,
            long startSurrogateId = 1,
            long endSurrogateId = 1000)
        {
            var resultList = new List<SearchResultEntry>();

            for (var i = 0; i < resourceCount; i++)
            {
                var wrapper = Substitute.For<ResourceWrapper>();
                var propertyInfo = wrapper.GetType().GetProperty("ResourceTypeName");
                propertyInfo?.SetValue(wrapper, "Patient");

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

        [Fact]
        public async Task ExecuteAsync_WithNullJobInfo_ThrowsArgumentNullException()
        {
            var orchestrator = CreateReindexOrchestratorJob();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => orchestrator.ExecuteAsync(null, _cancellationToken));
        }

        [Fact]
        public async Task ExecuteAsync_WithNoSearchParametersToProcess_ReturnsSuccessfulResult()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.NotNull(jobResult.Error);
            Assert.Contains(jobResult.Error, e => e.Diagnostics.Contains("Nothing to process"));
        }

        [Fact]
        public async Task ExecuteAsync_WhenCancellationRequested_ReturnsJobCancelledResult()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.CancelAfter(100); // Cancel after short delay

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
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(Task.FromException<IReadOnlyCollection<ResourceSearchParameterStatus>>(
                    new InvalidOperationException("Database error")));

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var initialError = new OperationOutcomeIssue(
                OperationOutcomeConstants.IssueSeverity.Information,
                OperationOutcomeConstants.IssueType.Informational,
                "Initial error");

            jobInfo.Result = JsonConvert.SerializeObject(new ReindexOrchestratorJobResult
            {
                Error = new List<OperationOutcomeIssue> { initialError },
            });

            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchParam = CreateSearchParameterInfo(
                baseResourceTypes: new List<string> { KnownResourceTypes.Resource });

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

            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task GetDerivedResourceTypes_WithDomainResourceBaseType_ReturnsApplicableResourceTypes()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchParam = CreateSearchParameterInfo(
                baseResourceTypes: new List<string> { KnownResourceTypes.DomainResource });

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

            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task CalculateAndSetTotalAndResourceCounts_WithMultipleResourceTypes_CalculatesCorrectly()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var searchResult = CreateSearchResult(resourceCount: 50);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.True(jobInfo.Data.HasValue);
            Assert.Equal(50, jobInfo.Data.Value);
        }

        [Fact]
        public async Task GetResourceCountForQueryAsync_WithValidQuery_ReturnsSearchResult()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var searchResult = CreateSearchResult(resourceCount: 100);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            await _searchService.Received().SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>());
        }

        [Fact]
        public async Task GetResourceCountForQueryAsync_WithSearchServiceException_ReturnsErrorResult()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(Task.FromException<SearchResult>(
                    new InvalidOperationException("Search service error")));

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.NotNull(jobResult.Error);
            Assert.Contains(jobResult.Error, e => e.Severity == OperationOutcomeConstants.IssueSeverity.Error);
        }

        [Fact]
        public async Task EnqueueQueryProcessingJobsAsync_WithValidSearchParameters_CreatesProcessingJobs()
        {
            // Arrange
            const int maxResourcePerQuery = 100;
            var paramHashMap = new Dictionary<string, string> { { "Patient", "patientHash" } };
            var jobRecord = new ReindexJobRecord(
                paramHashMap,
                new List<string>(),
                new List<string>(),
                new List<string>(),
                maxResourcePerQuery);

            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var ranges = new List<(long, long)>
            {
                (1, 100),
                (101, 200),
                (201, 250),
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
                .Returns(ranges);

            var orchestrator = CreateReindexOrchestratorJob(
                new AzureHealthDataServicesRuntimeConfiguration(),
                searchParameterCacheRefreshIntervalSeconds: 0);

            // Act: Fire off execute asynchronously without awaiting
            var executeTask = orchestrator.ExecuteAsync(jobInfo, _cancellationToken);

            // Wait for jobs to be created
            await Task.Delay(5000);

            // Assert: Check that processing jobs were created
            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
            var processingJobs = jobs.Where(j => j.Id != jobInfo.Id).ToList();
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
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
        public async Task UpdateSearchParameterStatus_WithPendingDeleteStatus_UpdatesToDeleted()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var searchResult = CreateSearchResult(resourceCount: 10);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
            var processingJobs = jobs.Where(j => j.Id != jobInfo.Id).ToList();

            if (processingJobs.Count > 0)
            {
                var firstJob = processingJobs.First();
                var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(firstJob.Definition);
                Assert.NotNull(jobDef.SearchParameterUrls);
            }
        }

        [Fact]
        public async Task GetValidSearchParameterUrlsForResourceType_WithExceptionInValidation_UsesFallback()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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
                .Returns(x => throw new Exception("Validation error"));

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task CheckJobRecordForAnyWork_WithZeroResources_ReturnsFalse()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            jobRecord.Count = 0;
            jobRecord.ResourceCounts.Clear();

            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var emptyStatus = new ReadOnlyCollection<ResourceSearchParameterStatus>(
                new List<ResourceSearchParameterStatus>());

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(emptyStatus);

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo>());

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task ProcessCompletedJobs_WithFailedJobs_HandlesFailed()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var searchResult = CreateSearchResult(resourceCount: 100);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task CalculateTotalCount_WithMultipleResourceTypes_CountsAllResources()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var searchResult = CreateSearchResult(resourceCount: 75);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.True(jobInfo.Data.HasValue);
            Assert.Equal(75, jobInfo.Data.Value);
        }

        [Fact]
        public async Task ProcessCompletedJobsAndDetermineReadiness_WithReadyParameters_ReturnsReadyList()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var emptySearchResult = new SearchResult(0, new List<Tuple<string, string>>());
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(emptySearchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task HandleException_LogsAndReturnsError()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(Task.FromException<IReadOnlyCollection<ResourceSearchParameterStatus>>(
                    new SystemException("Critical error")));

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
            var jobRecord = CreateReindexJobRecord(targetResourceTypes: targetResourceTypes);
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
            var processingJobs = jobs.Where(j => j.Id != jobInfo.Id).ToList();

            foreach (var job in processingJobs)
            {
                var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(job.Definition);
                Assert.Equal("Patient", jobDef.ResourceType);
            }
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithResourceTypesWithZeroCount_MarksParametersAsEnabled()
        {
            // Arrange
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

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
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);

            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, true, _cancellationToken);
            var processingJobs = jobs.Where(j => j.Id != jobInfo.Id).ToList();

            if (processingJobs.Count > 0)
            {
                var firstJob = processingJobs.First();
                var jobDef = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(firstJob.Definition);
                Assert.NotNull(jobDef.SearchParameterUrls);
            }
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithCaseVariantDifferentStatuses_ReturnsBothEntriesWithRespectiveStatuses()
        {
            // Arrange - Test case where we have case variant URLs with different statuses (Supported and PendingDelete)
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var searchResult = CreateSearchResult(resourceCount: 75);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);

            Assert.True(jobInfo.Data.HasValue);
            Assert.Equal(75, jobInfo.Data.Value);
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithMultipleCaseVariantsAndPendingDisable_MaintainsAllVariants()
        {
            // Arrange - Test with PendingDisable status
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);

            // Both case variants should be updated to Disabled status
            await _searchParameterStatusManager.Received().UpdateSearchParameterStatusAsync(
                Arg.Any<List<string>>(),
                SearchParameterStatus.Disabled,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task CheckForCompletionAsync_WithCaseVariantUrls_MaintainsBothVariants()
        {
            // Arrange - Simulate completion checking with case variant URLs
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

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

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
        }

        [Fact]
        public async Task CreateReindexProcessingJobsAsync_WithCaseVariantAndMixedStatuses_SkipsUnsupportedAndProcessesSupported()
        {
            // Arrange - Test with case variants where one is supported and one is not a valid status
            var jobRecord = CreateReindexJobRecord();
            var jobInfo = new JobInfo
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(jobRecord),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchParamSupported = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/searchparameter/condition-code",
                resourceType: "Condition");

            var searchParamLowercase = CreateSearchParameterInfo(
                url: "http://hl7.org/fhir/SearchParameter/Condition-Code",
                resourceType: "Condition");

            var searchParamStatus1 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParamSupported.Url.OriginalString),
                Status = SearchParameterStatus.Supported,
            };

            var searchParamStatus2 = new ResourceSearchParameterStatus
            {
                LastUpdated = DateTime.UtcNow,
                Uri = new Uri(searchParamLowercase.Url.OriginalString),
                Status = SearchParameterStatus.PendingDelete,
            };

            _searchParameterStatusManager.GetAllSearchParameterStatus(_cancellationToken)
                .Returns(new List<ResourceSearchParameterStatus> { searchParamStatus1, searchParamStatus2 });

            _searchDefinitionManager.AllSearchParameters
                .Returns(new List<SearchParameterInfo> { searchParamSupported, searchParamLowercase });

            _searchDefinitionManager.GetSearchParameters("Condition")
                .Returns(new List<SearchParameterInfo> { searchParamSupported, searchParamLowercase });

            _searchDefinitionManager.GetSearchParameter(Arg.Any<string>())
                .Returns(x => x[0] switch
                {
                    "http://hl7.org/fhir/searchparameter/condition-code" => searchParamSupported,
                    "http://hl7.org/fhir/SearchParameter/Condition-Code" => searchParamLowercase,
                    _ => throw new SearchParameterNotSupportedException("Not found"),
                });

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>())
                .Returns("hash");

            var searchResult = CreateSearchResult(resourceCount: 60);
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(searchResult);

            var orchestrator = CreateReindexOrchestratorJob(searchParameterCacheRefreshIntervalSeconds: 0);

            // Act
            var result = await orchestrator.ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(result);

            // Assert
            Assert.NotNull(jobResult);
            Assert.True(jobInfo.Data.HasValue);
            Assert.Equal(60, jobInfo.Data.Value);
        }
    }
}
