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
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.Reindex
{
    public class ReindexProcessingJobTests
    {
        private readonly string _base64EncodedToken = ContinuationTokenConverter.Encode("token");
        private const int _mockedSearchCount = 5;

        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IReindexUtilities _reindexUtilities = Substitute.For<IReindexUtilities>();
        private readonly IReindexJobThrottleController _throttleController = Substitute.For<IReindexJobThrottleController>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private Func<ReindexProcessingJob> _reindexProcessingJobTaskFactory;
        private readonly IQueueClient _queueClient = new TestQueueClient();
        private const uint BatchSize = 2U;
        private CancellationToken _cancellationToken;

        public ReindexProcessingJobTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            _throttleController.GetThrottleBasedDelay().Returns(0);
            _throttleController.GetThrottleBatchSize().Returns(BatchSize);
            _reindexProcessingJobTaskFactory = () =>
                 new ReindexProcessingJob(
                     () => _searchService.CreateMockScope(),
                     _reindexUtilities,
                     _throttleController,
                     NullLoggerFactory.Instance,
                     _queueClient);

            _reindexUtilities.UpdateSearchParameterStatusToEnabled(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(x => (true, null));
        }

        [Fact]
        public async Task GivenContinuationToken_WhenExecuted_ThenAdditionalQueryAdded()
        {
            var expectedResourceType = "Account";
            SearchParameterInfo searchParameterInfo = new SearchParameterInfo("Account", "status", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParam/Accout-status"));
            ReindexProcessingJobDefinition job = new ReindexProcessingJobDefinition()
            {
                ForceReindex = false,
                MaximumNumberOfResourcesPerQuery = 100,
                QueryDelayIntervalInMilliseconds = 0,
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
                TargetDataStoreUsagePercentage = 0,
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
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(_mockedSearchCount, new List<Tuple<string, string>>()), // First call checks how many resources need to be reindexed
                    new SearchResult(0, new List<Tuple<string, string>>())); // Second call checks that there are no resources left to be reindexed

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
            Returns(
                x => CreateSearchResult("token"),
                x => CreateSearchResult());

            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken));

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>(), true);

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true);

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(_mockedSearchCount, job.Count);
            Assert.Contains(expectedResourceType, job.ResourceList);
            Assert.Equal(param.Url.ToString(), job.SearchParamList);
            Assert.Collection<ReindexJobQueryStatus>(
                job.QueryList.Keys.OrderBy(q => q.LastModified),
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed),
                item2 => Assert.True(item2.ContinuationToken == _base64EncodedToken && item2.Status == OperationStatus.Completed));

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
