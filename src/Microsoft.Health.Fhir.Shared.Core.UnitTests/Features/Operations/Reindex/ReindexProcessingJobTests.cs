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
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
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

        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IReindexUtilities _reindexUtilities = Substitute.For<IReindexUtilities>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private Func<ReindexProcessingJob> _reindexProcessingJobTaskFactory;
        private readonly IQueueClient _queueClient = new TestQueueClient();
        private CancellationToken _cancellationToken;

        public ReindexProcessingJobTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            _reindexProcessingJobTaskFactory = () =>
                 new ReindexProcessingJob(
                     () => _searchService.CreateMockScope(),
                     _reindexUtilities,
                     NullLoggerFactory.Instance,
                     _queueClient);
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
                SearchParameterUrls = new List<string>() { "http://hl7.org/fhir/SearchParam/Accout-status" },
                TypeId = (int)JobType.ReindexProcessing,
            };

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
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,  // continuationToken
                    null,  // sortOrder
                    new List<Tuple<string, string>>())); // unsupportedSearchParameters

            // Also set up the reindex utilities mock
            _reindexUtilities
                .ProcessSearchResultsAsync(
                    Arg.Any<SearchResult>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(
                await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            // Verify ProcessSearchResultsAsync was called
            await _reindexUtilities.Received(1).ProcessSearchResultsAsync(
                Arg.Any<SearchResult>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());

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
            };

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
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
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

            var jobs = await _queueClient.GetJobByGroupIdAsync(
                (byte)QueueType.Reindex,
                jobInfo.GroupId,
                false,
                _cancellationToken);

            Assert.Single(jobs);
            var childJob = jobs.First();
            Assert.NotNull(childJob);
            Assert.Equal(jobInfo.GroupId, childJob.GroupId);

            var childJobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(
                childJob.Definition);

            Assert.Equal(2, childJobDefinition.ResourceCount.StartResourceSurrogateId);
            Assert.Equal(2, childJobDefinition.ResourceCount.EndResourceSurrogateId);
        }
    }
}
