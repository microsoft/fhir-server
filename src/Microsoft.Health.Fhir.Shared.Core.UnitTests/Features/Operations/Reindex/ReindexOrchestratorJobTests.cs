// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
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
        private Func<ReindexOrchestratorJob> _reindexJobTaskFactory;
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly IQueueClient _queueClient = new TestQueueClient();

        private ISearchParameterDefinitionManager _searchDefinitionManager;
        private CancellationToken _cancellationToken;

        public ReindexOrchestratorJobTests()
        {
            _cancellationToken = _cancellationTokenSource.Token;
            _searchDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterStatusmanager = Substitute.For<ISearchParameterStatusManager>();

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
            List<ResourceSearchParameterStatus> status = new List<ResourceSearchParameterStatus>()
            {
                new ResourceSearchParameterStatus()
                {
                    LastUpdated = DateTime.UtcNow,
                    Uri = new Uri("http://hl7.org/fhir/SearchParameter/Account-status"),
                    Status = SearchParameterStatus.Enabled,
                },
            };

            _searchParameterStatusmanager.GetAllSearchParameterStatus(Arg.Any<CancellationToken>()).Returns(status);
            _searchDefinitionManager.AllSearchParameters.Returns(searchParameterInfos);
            _reindexJobTaskFactory = () =>
                 new ReindexOrchestratorJob(
                     _queueClient,
                     () => _searchService.CreateMockScope(),
                     _searchDefinitionManager,
                     ModelInfoProvider.Instance,
                     _searchParameterStatusmanager,
                     _searchParameterOperations,
                     NullLoggerFactory.Instance);
        }

        [Fact]
        public async Task GivenSupportedParams_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            // Get one search parameter and configure it such that it needs to be reindexed
            var param = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri("http://hl7.org/fhir/SearchParameter/Account-status"));
            param.IsSearchable = false;
            var expectedResourceType = param.BaseResourceTypes.FirstOrDefault();

            ReindexJobRecord job = CreateReindexJobRecord();
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
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(_mockedSearchCount, new List<Tuple<string, string>>()));

            _searchService.SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 != expectedResourceType)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(0, new List<Tuple<string, string>>()));

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
                Returns(CreateSearchResult());
            try
            {
                var jobResult = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(await _reindexJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken));
            }
            catch (RetriableJobException ex)
            {
                Assert.Equal("Reindex job with Id: 1 has been started. Status: Running.", ex.Message);
            }

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), Arg.Is(true), Arg.Any<CancellationToken>(), true);

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t2 => t2.Item1 == "_count" && t2.Item2 == job.MaximumNumberOfResourcesPerQuery.ToString()) && l.Any(t => t.Item1 == "_type" && t.Item2 == expectedResourceType)),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true);

            var reindexJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, 1, true, Arg.Any<CancellationToken>());
            var processingJob = reindexJobs.FirstOrDefault();

            Assert.Equal(1, reindexJobs.Count);
            Assert.NotNull(processingJob);

            var processingJobDefinition = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(processingJob.Definition);
            Assert.Equal(_mockedSearchCount, processingJobDefinition.ResourceCount.Count);
            Assert.Equal(expectedResourceType, processingJobDefinition.ResourceType);
            Assert.Contains(param.Url.ToString(), processingJobDefinition.SearchParameterUrls);

            param.IsSearchable = true;
        }

        [Fact]
        public async Task GivenNoSupportedParams_WhenExecuted_ThenJobCompletesWithNoWork()
        {
            var job = CreateReindexJobRecord();
            JobInfo jobInfo = new JobInfo()
            {
                Id = 3,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 3,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var result = JsonConvert.DeserializeObject<ReindexOrchestratorJobResult>(await _reindexJobTaskFactory().ExecuteAsync(jobInfo, Substitute.For<IProgress<string>>(), _cancellationToken));

            Assert.Equal("Nothing to process. Reindex complete.", result.Error.First().Diagnostics);
            var jobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, jobInfo.GroupId, false, _cancellationToken);
            Assert.False(jobs.Any());
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

        private ReindexJobRecord CreateReindexJobRecord(uint maxResourcePerQuery = 100, IReadOnlyDictionary<string, string> paramHashMap = null)
        {
            if (paramHashMap == null)
            {
                paramHashMap = new Dictionary<string, string>() { { "Patient", "patientHash" } };
            }

            return new ReindexJobRecord(paramHashMap, new List<string>(), new List<string>(), new List<string>(), maxiumumConcurrency: 1, maxResourcePerQuery);
        }
    }
}
