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
                ForceReindex = false,
                MaximumNumberOfResourcesPerQuery = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    CountReindexed = 0,
                    CurrentResourceSurrogateId = 0,
                    EndResourceSurrogateId = 1,
                    StartResourceSurrogateId = 0,
                    ContinuationToken = null,
                },
                ResourceTypeSearchParameterHashMap = "accountHash",
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

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(_mockedSearchCount, new List<Tuple<string, string>>())); // First call checks how many resources need to be reindexed

            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken));

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true);

            Assert.Equal(_mockedSearchCount, result.SucceededResourceCount);
        }

        [Fact]
        public async Task GivenContinuationToken_WhenExecuted_ThenAdditionalQueryAdded()
        {
            var expectedResourceType = "Account";
            ReindexProcessingJobDefinition job = new ReindexProcessingJobDefinition()
            {
                ForceReindex = false,
                MaximumNumberOfResourcesPerQuery = 1,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    CountReindexed = 0,
                    CurrentResourceSurrogateId = 0,
                    EndResourceSurrogateId = 2,
                    StartResourceSurrogateId = 0,
                    ContinuationToken = "continuationToken",
                },
                ResourceTypeSearchParameterHashMap = "accountHash",
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

            // setup search result
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
                        "continuationToken",
                        new List<(SearchParameterInfo, SortOrder)>(),
                        new List<Tuple<string, string>>())
                    {
                        MaxResourceSurrogateId = 1,
                        TotalCount = 1,
                    });

            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken));

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true);

            Assert.Equal(1, result.SucceededResourceCount);
            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, false, _cancellationToken);
            Assert.Equal(1, jobs.Count);
            var childJob = jobs.First();
            Assert.NotNull(childJob);
            Assert.Equal(jobInfo.GroupId, childJob.GroupId);
            var childJobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(childJob.Definition);
            Assert.Equal(2, childJobDefinition.ResourceCount.StartResourceSurrogateId);
            Assert.Equal(2, childJobDefinition.ResourceCount.CurrentResourceSurrogateId);
            Assert.Equal(2, childJobDefinition.ResourceCount.EndResourceSurrogateId);
            Assert.Equal(job.ResourceCount.ContinuationToken, childJobDefinition.ResourceCount.ContinuationToken);
        }

        /*
        [Fact]
        public async Task GivenQueryInRunningState_WhenExecuted_ThenQueryResetToQueuedOnceStale()
        {
            // Add one parameter that needs to be indexed
            var param = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Code == "appointment");
            param.IsSearchable = false;

            _reindexJobConfiguration.JobHeartbeatTimeoutThreshold = new TimeSpan(0, 0, 0, 1, 0);

            ReindexJobRecord job = CreateReindexJobRecord(maxResourcePerQuery: 3);
            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            job.QueryList.TryAdd(new ReindexJobQueryStatus("Patient", "token") { Status = OperationStatus.Running }, 1);
            job.Resources.Add("Patient");
            job.ResourceCounts.TryAdd("Patient", new SearchResultReindex()
            {
                Count = 1,
                CurrentResourceSurrogateId = 1,
                EndResourceSurrogateId = 1,
                StartResourceSurrogateId = 1,
            });
            JobInfo jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // setup search results
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    x => CreateSearchResult("token1", 3),
                    x => CreateSearchResult("token2", 3),
                    x => CreateSearchResult("token3", 3),
                    x => CreateSearchResult("token4", 3),
                    x => CreateSearchResult(null, 2));

            await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken);

            param.IsSearchable = true;

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(_mockedSearchCount, job.QueryList.Count);
        }

        [Fact]
        public async Task GivenQueryWhichContinuallyFails_WhenExecuted_ThenJobWillBeMarkedFailed()
        {
            // Add one parameter that needs to be indexed
            var param = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Code == "appointment");
            param.IsSearchable = false;

            var job = CreateReindexJobRecord(maxResourcePerQuery: 3);
            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            job.QueryList.TryAdd(new ReindexJobQueryStatus("Patient", "token") { Status = OperationStatus.Running }, 1);
            JobInfo jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // setup search results
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult(null, 2));

            _reindexUtilities.ProcessSearchResultsAsync(Arg.Any<SearchResult>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
                .Throws(new Exception("Failed to process query"));

            await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken);

            param.IsSearchable = true;

            Assert.Equal(_reindexJobConfiguration.ConsecutiveFailuresThreshold, job.QueryList.Keys.First().FailureCount);
            Assert.Equal(OperationStatus.Failed, job.Status);
        }
        */

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
    }
}
