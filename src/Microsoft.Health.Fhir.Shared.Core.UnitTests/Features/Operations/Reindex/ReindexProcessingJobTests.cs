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

        private SearchResult CreateSearchResult(string continuationToken = null, int resourceCount = 1)
        {
            var resultList = new List<SearchResultEntry>();

            for (var i = 0; i < resourceCount; i++)
            {
                var wrapper = Substitute.For<ResourceWrapper>();
                var entry = new SearchResultEntry(wrapper);
                resultList.Add(entry);
            }

            var searchResult = new SearchResult(resultList, continuationToken, null, new List<Tuple<string, string>>());
            searchResult.MaxResourceSurrogateId = 1;
            searchResult.TotalCount = resultList.Count;
            return searchResult;
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
        public async Task GivenSurrogateIdRange_WhenHashDoesNotMatch_ThenRaiseError()
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

            // Injecting a different hash to trigger the update logic and subsequent failure.
            _searchParameterOperations.GetResourceTypeSearchParameterHashMap(Arg.Any<string>()).Returns(job.ResourceTypeSearchParameterHashMap + "_trash");

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

            ReindexProcessingJobSoftException exception = await Assert.ThrowsAsync<ReindexProcessingJobSoftException>(
                async () => await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            Assert.Contains("Search Parameter hash does not match. Resubmit reindex job to try again.", exception.Message);
        }
    }
}
