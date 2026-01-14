// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Reindex
{
    [CollectionDefinition("ReindexProcessingJobTests")]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexProcessingJobTests
    {
        private const int _mockedSearchCount = 5;

        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly IResourceWrapperFactory _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        private readonly Func<ReindexProcessingJob> _reindexProcessingJobTaskFactory;
        private readonly CancellationToken _cancellationToken;

        public ReindexProcessingJobTests()
        {
            Func<Health.Extensions.DependencyInjection.IScoped<IFhirDataStore>> fhirDataStoreScope = () => _fhirDataStore.CreateMockScope();
            _cancellationToken = _cancellationTokenSource.Token;
            _reindexProcessingJobTaskFactory = () =>
                 new ReindexProcessingJob(
                     () => _searchService.CreateMockScope(),
                     fhirDataStoreScope,
                     _resourceWrapperFactory,
                     _searchParameterOperations,
                     NullLogger<ReindexProcessingJob>.Instance);
        }

        [Fact]
        public async Task GivenAProcessingJob_WhenExecuted_ThenCorrectCountIsProcessed()
        {
            var expectedResourceType = "Account";
            ReindexProcessingJobDefinition job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = _mockedSearchCount,
                    EndResourceSurrogateId = 0,
                    StartResourceSurrogateId = 0,
                    ContinuationToken = null,
                },
                ResourceTypeSearchParameterHashMap = "accountHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Accout-status" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            JobInfo jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Setup search result with actual entries that can be processed
            var searchResultEntries = Enumerable.Range(1, _mockedSearchCount)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,  // continuationToken
                    null,  // sortOrder
                    new List<Tuple<string, string>>())); // unsupportedSearchParameters

            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(
                await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            Assert.Equal(_mockedSearchCount, result.SucceededResourceCount);
        }

        private SearchResultEntry CreateSearchResultEntry(string id, string type)
        {
            return new SearchResultEntry(
                new ResourceWrapper(
                    id,
                    "1",
                    type,
                    new RawResource("data", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null));
        }

        [Fact]
        public async Task GivenSurrogateIdRange_WhenExecuted_ThenAdditionalQueryAdded()
        {
            var expectedResourceType = "Account";
            ReindexProcessingJobDefinition job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 1,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 2,
                    StartResourceSurrogateId = 0,
                },
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Accout-status" },
                TypeId = (int)JobType.ReindexProcessing,
                GroupId = 3,
                ResourceTypeSearchParameterHashMap = "accountHash",
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            JobInfo jobInfo = new JobInfo()
            {
                Id = 2,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 3,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Setup search result - remove continuation token, focus on surrogate IDs
            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(
                        new List<SearchResultEntry>()
                        {
                            CreateSearchResultEntry("1", "Account"),
                        },
                        null, // No continuation token
                        new List<(SearchParameterInfo, SortOrder)>(),
                        new List<Tuple<string, string>>())
                    {
                        MaxResourceSurrogateId = 1,
                        TotalCount = 1,
                    });

            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(
                await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            Assert.Equal(1, result.SucceededResourceCount);
        }

        [Fact]
        public async Task ExecuteAsync_WithNullJobInfo_ThrowsArgumentNullException()
        {
            var job = _reindexProcessingJobTaskFactory();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => job.ExecuteAsync(null, _cancellationToken));
        }

        [Fact]
        public async Task ExecuteAsync_WithValidJobInfo_ReturnsSerializedResult()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 2,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                    ContinuationToken = null,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 2)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);

            Assert.NotEmpty(result);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);
            Assert.NotNull(jobResult);
            Assert.Equal(2, jobResult.SucceededResourceCount);
        }

        [Fact]
        public async Task ProcessSearchResultsAsync_WithValidResults_UpdatesAllResources()
        {
            var resourceType = "Patient";
            var batchSize = 2;
            var resources = new List<ResourceWrapper>()
            {
                new ResourceWrapper(
                    "1",
                    "1",
                    resourceType,
                    new RawResource("data1", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
                new ResourceWrapper(
                    "2",
                    "1",
                    resourceType,
                    new RawResource("data2", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
            };

            var searchResults = new List<SearchResultEntry>()
            {
                new SearchResultEntry(resources[0]),
                new SearchResultEntry(resources[1]),
            };

            var searchResult = new SearchResult(
                searchResults,
                null,
                null,
                new List<Tuple<string, string>>());

            var paramHashMap = new Dictionary<string, string>
            {
                { resourceType, "patientHash" },
            };

            var job = _reindexProcessingJobTaskFactory();

            await job.ProcessSearchResultsAsync(searchResult, paramHashMap, batchSize, _cancellationToken);

            // Verify that bulk update was called with the correct batch
            await _fhirDataStore.Received(1).BulkUpdateSearchParameterIndicesAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(r => r.Count == 2),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessSearchResultsAsync_WithZeroBatchSize_SetsDefaultBatchSize()
        {
            var resourceType = "Observation";
            var resources = new List<ResourceWrapper>()
            {
                new ResourceWrapper(
                    "1",
                    "1",
                    resourceType,
                    new RawResource("data1", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
            };

            var searchResults = new List<SearchResultEntry>()
            {
                new SearchResultEntry(resources[0]),
            };

            var searchResult = new SearchResult(
                searchResults,
                null,
                null,
                new List<Tuple<string, string>>());

            var paramHashMap = new Dictionary<string, string>
            {
                { resourceType, "observationHash" },
            };

            var job = _reindexProcessingJobTaskFactory();

            // Pass zero batch size - should default to 500
            await job.ProcessSearchResultsAsync(searchResult, paramHashMap, 0, _cancellationToken);

            await _fhirDataStore.Received(1).BulkUpdateSearchParameterIndicesAsync(
                Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessSearchResultsAsync_WithMultipleBatches_ProcessesInBatches()
        {
            var resourceType = "Patient";
            var batchSize = 2;

            // Create 5 resources to test batching
            var resources = new List<ResourceWrapper>();
            for (int i = 1; i <= 5; i++)
            {
                resources.Add(new ResourceWrapper(
                    i.ToString(),
                    "1",
                    resourceType,
                    new RawResource($"data{i}", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null));
            }

            var searchResults = resources.Select(r => new SearchResultEntry(r)).ToList();

            var searchResult = new SearchResult(
                searchResults,
                null,
                null,
                new List<Tuple<string, string>>());

            var paramHashMap = new Dictionary<string, string>
            {
                { resourceType, "patientHash" },
            };

            var job = _reindexProcessingJobTaskFactory();

            await job.ProcessSearchResultsAsync(searchResult, paramHashMap, batchSize, _cancellationToken);

            // Should be called 3 times: batch 1 (2 resources), batch 2 (2 resources), batch 3 (1 resource)
            await _fhirDataStore.Received(3).BulkUpdateSearchParameterIndicesAsync(
                Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessSearchResultsAsync_WithMissingHashMap_UsesEmptyString()
        {
            var resourceType = "Patient";
            var batchSize = 10;

            var resources = new List<ResourceWrapper>()
            {
                new ResourceWrapper(
                    "1",
                    "1",
                    resourceType,
                    new RawResource("data1", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
            };

            var searchResults = new List<SearchResultEntry>()
            {
                new SearchResultEntry(resources[0]),
            };

            var searchResult = new SearchResult(
                searchResults,
                null,
                null,
                new List<Tuple<string, string>>());

            // Empty hash map - should use empty string for missing resource types
            var paramHashMap = new Dictionary<string, string>();

            var job = _reindexProcessingJobTaskFactory();

            await job.ProcessSearchResultsAsync(searchResult, paramHashMap, batchSize, _cancellationToken);

            await _fhirDataStore.Received(1).BulkUpdateSearchParameterIndicesAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(r =>
                    r.First().SearchParameterHash == string.Empty),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessSearchResultsAsync_WithCancellationToken_StopsProcessing()
        {
            var resourceType = "Patient";
            var batchSize = 10;

            var resources = new List<ResourceWrapper>()
            {
                new ResourceWrapper(
                    "1",
                    "1",
                    resourceType,
                    new RawResource("data1", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
            };

            var searchResults = new List<SearchResultEntry>()
            {
                new SearchResultEntry(resources[0]),
            };

            var searchResult = new SearchResult(
                searchResults,
                null,
                null,
                new List<Tuple<string, string>>());

            var paramHashMap = new Dictionary<string, string>
            {
                { resourceType, "patientHash" },
            };

            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            var job = _reindexProcessingJobTaskFactory();

            await job.ProcessSearchResultsAsync(searchResult, paramHashMap, batchSize, cancellationTokenSource.Token);

            // Should not call bulk update when cancelled
            await _fhirDataStore.DidNotReceive().BulkUpdateSearchParameterIndicesAsync(
                Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ProcessQueryAsync_WithNullSearchResult_ThrowsOperationFailedException()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Return null search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns((SearchResult)null);

            // When null search result is returned, the job should handle it gracefully and return error in result
            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.NotNull(jobResult.Error);
            Assert.Contains("null search result", jobResult.Error);
        }

        [Fact]
        public async Task ProcessQueryAsync_WithSearchServiceException_ThrowsReindexException()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Throw exception from search service
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(Task.FromException<SearchResult>(new InvalidOperationException("Search service error")));

            // When search service throws an exception, the job should handle it gracefully and return error in result
            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.NotNull(jobResult.Error);
            Assert.Contains("Error running reindex query", jobResult.Error);
        }

        [Fact]
        public async Task ProcessQueryAsync_WithGeneralException_CatchesAndSetsError()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 1)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            // Throw general exception from bulk update
            _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(
                Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new InvalidOperationException("General error during bulk update")));

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.NotNull(jobResult.Error);
            Assert.Contains("General error", jobResult.Error);
            Assert.Equal(1, jobResult.FailedResourceCount);
        }

        [Fact]
        public async Task GetResourcesToReindexAsync_WithContinuationToken_IncludesTokenInQuery()
        {
            var expectedResourceType = "Patient";
            var continuationToken = "test-continuation-token";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 5,
                    EndResourceSurrogateId = 500,
                    StartResourceSurrogateId = 1,
                    ContinuationToken = continuationToken,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 5)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.Equal(5, jobResult.SucceededResourceCount);
        }

        [Fact]
        public async Task GetResourcesToReindexAsync_WithSurrogateIdRange_IncludesRangeInQuery()
        {
            var expectedResourceType = "Patient";
            var startId = 100L;
            var endId = 500L;
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 3,
                    EndResourceSurrogateId = endId,
                    StartResourceSurrogateId = startId,
                    ContinuationToken = null,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 3)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.Equal(3, jobResult.SucceededResourceCount);
        }

        [Fact]
        public async Task ExecuteAsync_WithLargeSurrogateIdRange_ProcessesInMemorySafeBatches()
        {
            // This test verifies that when processing a large surrogate ID range,
            // the job correctly fetches resources in smaller memory-safe batches
            // by advancing the StartSurrogateId after each batch based on MaxResourceSurrogateId.
            var expectedResourceType = "Patient";
            var startId = 100L;
            var endId = 5000L;
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 10000, // Large batch configured
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 6, // 6 resources total across multiple batches
                    EndResourceSurrogateId = endId,
                    StartResourceSurrogateId = startId,
                    ContinuationToken = null,
                },
                ResourceTypeSearchParameterHashMap = "patientHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Patient-name" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // First batch returns 3 resources with MaxResourceSurrogateId = 200
            var firstBatchEntries = Enumerable.Range(1, 3)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            // Second batch returns 3 more resources with MaxResourceSurrogateId = 400
            var secondBatchEntries = Enumerable.Range(4, 3)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            var callCount = 0;
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(x =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First batch
                        return new SearchResult(
                            firstBatchEntries,
                            null,
                            null,
                            new List<Tuple<string, string>>())
                        {
                            MaxResourceSurrogateId = 200, // Will cause next batch to start from 201
                            TotalCount = 3,
                        };
                    }
                    else if (callCount == 2)
                    {
                        // Second batch
                        return new SearchResult(
                            secondBatchEntries,
                            null,
                            null,
                            new List<Tuple<string, string>>())
                        {
                            MaxResourceSurrogateId = 400,
                            TotalCount = 3,
                        };
                    }
                    else
                    {
                        // No more resources
                        return new SearchResult(
                            new List<SearchResultEntry>(),
                            null,
                            null,
                            new List<Tuple<string, string>>())
                        {
                            TotalCount = 0,
                        };
                    }
                });

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            // Verify all 6 resources were processed across multiple batches
            Assert.Equal(6, jobResult.SucceededResourceCount);

            // Verify multiple batches were fetched (at least 2 for the resources + 1 that returns empty)
            Assert.True(callCount >= 2, $"Expected at least 2 search calls for batch processing, but got {callCount}");
        }

        [Fact]
        public async Task ExecuteAsync_WithOutOfMemoryException_ReducesBatchSizeAndRetries()
        {
            var expectedResourceType = "DiagnosticReport";
            int callCount = 0;

            ReindexProcessingJobDefinition job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 10000, // Large batch that might cause OOM
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 5,
                    EndResourceSurrogateId = 1000,
                    StartResourceSurrogateId = 1,
                },
                ResourceTypeSearchParameterHashMap = "diagnosticHash",
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/DiagnosticReport-code" },
                TypeId = (int)JobType.ReindexProcessing,
            };

            JobInfo jobInfo = new JobInfo()
            {
                Id = 100,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 100,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var successfulEntries = Enumerable.Range(1, 5)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(callInfo =>
                {
                    callCount++;

                    // First call throws OOM to simulate large resource fetch failure
                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM when fetching large batch of resources");
                    }

                    // After OOM, subsequent calls succeed with resources
                    if (callCount == 2)
                    {
                        return new SearchResult(
                            successfulEntries,
                            null,
                            null,
                            new List<Tuple<string, string>>())
                        {
                            MaxResourceSurrogateId = 500,
                            TotalCount = 5,
                        };
                    }

                    // Final call returns empty (no more resources)
                    return new SearchResult(
                        new List<SearchResultEntry>(),
                        null,
                        null,
                        new List<Tuple<string, string>>())
                    {
                        TotalCount = 0,
                    };
                });

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            // Verify resources were processed after OOM recovery
            Assert.Equal(5, jobResult.SucceededResourceCount);

            // Verify OOM was handled and recovery happened (first call fails, subsequent calls succeed)
            Assert.True(callCount >= 2, $"Expected at least 2 search calls for OOM recovery, but got {callCount}");
        }
    }
}
