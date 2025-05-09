﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
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
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Reindex
{
    [CollectionDefinition("ReindexTaskTests", DisableParallelization = true)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    public class ReindexJobTaskTests : IClassFixture<SearchParameterFixtureData>, IAsyncLifetime
    {
        private readonly string _base64EncodedToken = ContinuationTokenEncoder.Encode("token");
        private const int _mockedSearchCount = 5;

        private static readonly WeakETag _weakETag = WeakETag.FromVersionId("0");

        private readonly SearchParameterFixtureData _fixture;
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IFhirDataStore _fhirDataStore = Substitute.For<IFhirDataStore>();
        private readonly ReindexJobConfiguration _reindexJobConfiguration = new ReindexJobConfiguration();
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly IReindexUtilities _reindexUtilities = Substitute.For<IReindexUtilities>();
        private readonly IReindexJobThrottleController _throttleController = Substitute.For<IReindexJobThrottleController>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private Func<IReindexJobTask> _reindexJobTaskFactory;
        private SearchParameterStatusManager _searchParameterStatusmanager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();

        private SearchParameterDefinitionManager _searchDefinitionManager;
        private CancellationToken _cancellationToken;

        public ReindexJobTaskTests(SearchParameterFixtureData fixture) => _fixture = fixture;

        public async Task InitializeAsync()
        {
            _cancellationToken = _cancellationTokenSource.Token;

            _searchDefinitionManager = await SearchParameterFixtureData.CreateSearchParameterDefinitionManagerAsync(new VersionSpecificModelInfoProvider(), _mediator);
            _searchParameterStatusmanager = await SearchParameterFixtureData.CreateSearchParameterStatusManagerAsync(new VersionSpecificModelInfoProvider(), _mediator);
            var supportedSearchDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchDefinitionManager);
            var job = CreateReindexJobRecord();

            _fhirOperationDataStore.UpdateReindexJobAsync(job, _weakETag, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            _throttleController.GetThrottleBasedDelay().Returns(0);
            _reindexJobTaskFactory = () =>
                 new ReindexJobTask(
                     () => _fhirOperationDataStore.CreateMockScope(),
                     () => _fhirDataStore.CreateMockScope(),
                     Options.Create(_reindexJobConfiguration),
                     () => _searchService.CreateMockScope(),
                     supportedSearchDefinitionManager,
                     _reindexUtilities,
                     _contextAccessor,
                     _throttleController,
                     ModelInfoProvider.Instance,
                     NullLogger<ReindexJobTask>.Instance,
                     _searchParameterStatusmanager);

            _reindexUtilities.UpdateSearchParameterStatus(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(x => (true, null));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [Fact]
        public async Task GivenSupportedParams_WhenExecuted_ThenCorrectSearchIsPerformed()
        {
            // Get one search parameter and configure it such that it needs to be reindexed
            var param = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri("http://hl7.org/fhir/SearchParameter/Account-status"));
            await SetSearchParameterStatus(new ReadOnlyCollection<string>(new List<string>() { param.Url.OriginalString }), SearchParameterStatus.Supported);
            var expectedResourceType = param.BaseResourceTypes.FirstOrDefault();

            ReindexJobRecord job = CreateReindexJobRecord();
            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
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
                Returns(CreateSearchResult());

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received().SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), Arg.Is(true), Arg.Any<CancellationToken>(), true);

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Where(t => t.Item1 == "_type" && t.Item2 == expectedResourceType).Any()),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true);

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(_mockedSearchCount, job.Count);
            Assert.Equal(expectedResourceType, job.ResourceList);
            Assert.Equal(param.Url.ToString(), job.SearchParamList);
            Assert.Collection(
                job.QueryList.Keys,
                item => Assert.True(item.ContinuationToken == null && item.Status == OperationStatus.Completed));

            param.IsSearchable = true;
        }

        [Fact]
        public async Task GivenContinuationToken_WhenExecuted_ThenAdditionalQueryAdded()
        {
            // Get one search parameter and configure it such that it needs to be reindexed
            var param = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Code == "identifier");
            await SetSearchParameterStatus(new ReadOnlyCollection<string>(new List<string>() { param.Url.OriginalString }), SearchParameterStatus.Supported);
            var expectedResourceType = param.BaseResourceTypes.FirstOrDefault();

            ReindexJobRecord job = CreateReindexJobRecord();
            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
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

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

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

            param.IsSearchable = true;
        }

        [Fact]
        public async Task GivenRunningJob_WhenExecuted_ThenQueuedQueryCompleted()
        {
#if Stu3 || R4 || R4B
            var appointmentBaseTypeUri = "http://hl7.org/fhir/SearchParameter/Appointment-date";
#else
            var appointmentBaseTypeUri = "http://hl7.org/fhir/SearchParameter/Appointment-location";
#endif

            // Get two search parameters with different base resource types and configure them such that they need to be reindexed
            var paramWithAppointmentResponseBaseType = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri("http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment"));
            var paramWithAppointmentBaseType = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri(appointmentBaseTypeUri));

            Assert.NotNull(paramWithAppointmentResponseBaseType);
            Assert.NotNull(paramWithAppointmentBaseType);

            await SetSearchParameterStatus(new ReadOnlyCollection<string>(new List<string>() { paramWithAppointmentResponseBaseType.Url.OriginalString, paramWithAppointmentBaseType.Url.OriginalString }), SearchParameterStatus.Supported);
            var resourceTypeSearchParamHashMap = new Dictionary<string, string>
            {
                { "Appointment", "appointmentHash" },
                { "AppointmentResponse", "appointmentResponseHash" },
            };

            ReindexJobRecord job = CreateReindexJobRecord(paramHashMap: resourceTypeSearchParamHashMap);
            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(_mockedSearchCount, new List<Tuple<string, string>>()), // First two calls check how many resources need to be reindexed
                    new SearchResult(_mockedSearchCount, new List<Tuple<string, string>>()),
                    new SearchResult(0, new List<Tuple<string, string>>()), // Last two calls check that there are no resources left to be reindexed
                    new SearchResult(0, new List<Tuple<string, string>>()));

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    x => CreateSearchResult("token"),
                    x => CreateSearchResult("token"),
                    x => CreateSearchResult(),
                    x => CreateSearchResult());

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            // verify search for count
            await _searchService.Received(4).SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), true, Arg.Any<CancellationToken>(), true);

            // verify search for results
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_type" && t.Item2 == "Appointment")),
                Arg.Is<string>("appointmentHash"),
                false,
                Arg.Any<CancellationToken>(),
                true);

            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(l => l.Any(t => t.Item1 == "_type" && t.Item2 == "AppointmentResponse")),
                Arg.Is<string>("appointmentResponseHash"),
                false,
                Arg.Any<CancellationToken>(),
                true);

            // verify search for results with token
            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(
                    l => l.Any(t => t.Item1 == "_type" && t.Item2 == "Appointment") &&
                         l.Any(t => t.Item1 == KnownQueryParameterNames.ContinuationToken && t.Item2 == _base64EncodedToken)),
                Arg.Is<string>("appointmentHash"),
                false,
                Arg.Any<CancellationToken>(),
                true);

            await _searchService.Received().SearchForReindexAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(
                    l => l.Any(t => t.Item1 == "_type" && t.Item2 == "AppointmentResponse") &&
                         l.Any(t => t.Item1 == KnownQueryParameterNames.ContinuationToken && t.Item2 == _base64EncodedToken)),
                Arg.Is<string>("appointmentResponseHash"),
                false,
                Arg.Any<CancellationToken>(),
                true);

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(_mockedSearchCount * 2, job.Count);
            Assert.Equal(4, job.Progress);
            Assert.Contains("Appointment", job.ResourceList);
            Assert.Contains("AppointmentResponse", job.ResourceList);
            Assert.Contains("http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment", job.SearchParamList);
            Assert.Contains(appointmentBaseTypeUri, job.SearchParamList);

            Assert.Equal(4, job.QueryList.Count);
            Assert.Contains(job.QueryList.Keys, item => item.ContinuationToken == null && item.Status == OperationStatus.Completed && item.ResourceType == "AppointmentResponse");
            Assert.Contains(job.QueryList.Keys, item => item.ContinuationToken == null && item.Status == OperationStatus.Completed && item.ResourceType == "Appointment");
            Assert.Contains(job.QueryList.Keys, item => item.ContinuationToken == _base64EncodedToken && item.Status == OperationStatus.Completed && item.ResourceType == "AppointmentResponse");
            Assert.Contains(job.QueryList.Keys, item => item.ContinuationToken == _base64EncodedToken && item.Status == OperationStatus.Completed && item.ResourceType == "Appointment");

            await _reindexUtilities.Received().UpdateSearchParameterStatus(
                Arg.Is<IReadOnlyCollection<string>>(r => r.Any(s => s.Contains("Appointment")) && r.Any(s => s.Contains("AppointmentResponse"))),
                Arg.Any<CancellationToken>());

            paramWithAppointmentResponseBaseType.IsSearchable = true;
            paramWithAppointmentBaseType.IsSearchable = true;
        }

        [Fact]
        public async Task GivenNoSupportedParams_WhenExecuted_ThenJobCanceled()
        {
            var job = CreateReindexJobRecord();
            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Canceled, job.Status);
            await _searchService.DidNotReceiveWithAnyArgs().SearchForReindexAsync(default, default, default, default);
        }

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

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

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

            // setup search results
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>()).
                Returns(CreateSearchResult(null, 2));

            _reindexUtilities.ProcessSearchResultsAsync(Arg.Any<SearchResult>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<CancellationToken>())
                .Throws(new Exception("Failed to process query"));

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            param.IsSearchable = true;

            Assert.Equal(_reindexJobConfiguration.ConsecutiveFailuresThreshold, job.QueryList.Keys.First().FailureCount);
            Assert.Equal(OperationStatus.Failed, job.Status);
        }

        [Fact]
        public async Task GivenJobWithNoWork_WhenExecuted_ThenJobCompletedAndSPStatusUpdated()
        {
#if Stu3 || R4 || R4B
            var appointmentBaseTypeUri = "http://hl7.org/fhir/SearchParameter/Appointment-date";
#else
            var appointmentBaseTypeUri = "http://hl7.org/fhir/SearchParameter/Appointment-location";
#endif

            // Get two search parameters with different base resource types and configure them such that they need to be reindexed
            var paramWithAppointmentResponseBaseType = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri("http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment"));
            var paramWithAppointmentBaseType = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri(appointmentBaseTypeUri));

            Assert.NotNull(paramWithAppointmentResponseBaseType);
            Assert.NotNull(paramWithAppointmentBaseType);

            await SetSearchParameterStatus(new ReadOnlyCollection<string>(new List<string>() { paramWithAppointmentResponseBaseType.Url.OriginalString, paramWithAppointmentBaseType.Url.OriginalString }), SearchParameterStatus.Supported);
            var resourceTypeSearchParamHashMap = new Dictionary<string, string>
            {
                { "Appointment", "appointmentHash" },
                { "AppointmentResponse", "appointmentResponseHash" },
            };

            ReindexJobRecord job = CreateReindexJobRecord(paramHashMap: resourceTypeSearchParamHashMap);

            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(0, new List<Tuple<string, string>>()), // First two calls check how many resources need to be reindexed
                    new SearchResult(0, new List<Tuple<string, string>>()));

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(0, job.Count);
            Assert.Contains("Appointment", job.ResourceList);
            Assert.Contains("AppointmentResponse", job.ResourceList);
            Assert.Contains("http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment", job.SearchParamList);
            Assert.Contains(appointmentBaseTypeUri, job.SearchParamList);

            Assert.Empty(job.QueryList);

            await _reindexUtilities.Received().UpdateSearchParameterStatus(
                Arg.Is<IReadOnlyCollection<string>>(r => r.Any(s => s.Contains("Appointment")) && r.Any(s => s.Contains("AppointmentResponse"))),
                Arg.Any<CancellationToken>());

            paramWithAppointmentResponseBaseType.IsSearchable = true;
            paramWithAppointmentBaseType.IsSearchable = true;
        }

        [Fact]
        public async Task GivenForceReindex_WhenExecuted_ThenJobCompletedAndSPStatusUpdated()
        {
#if Stu3 || R4 || R4B
            var appointmentBaseTypeUri = "http://hl7.org/fhir/SearchParameter/Appointment-date";
#else
            var appointmentBaseTypeUri = "http://hl7.org/fhir/SearchParameter/Appointment-location";
#endif

            // Get two search parameters with different base resource types and configure them such that they need to be reindexed
            var paramWithAppointmentResponseBaseType = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri("http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment"));
            var paramWithAppointmentBaseType = _searchDefinitionManager.AllSearchParameters.FirstOrDefault(p => p.Url == new Uri(appointmentBaseTypeUri));

            Assert.NotNull(paramWithAppointmentResponseBaseType);
            Assert.NotNull(paramWithAppointmentBaseType);

            var resourceTypeSearchParamHashMap = new Dictionary<string, string>();
            resourceTypeSearchParamHashMap.Add("Appointment", "appointmentHash");
            resourceTypeSearchParamHashMap.Add("AppointmentResponse", "appointmentResponseHash");

            ReindexJobRecord job = CreateReindexJobRecord(
                paramHashMap: resourceTypeSearchParamHashMap,
                searchParameterTypes: new List<string>()
                {
                    "http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment",
                    appointmentBaseTypeUri,
                },
                resourceTypes: new List<string>()
                {
                    "Appointment",
                    "AppointmentResponse",
                });

            _fhirOperationDataStore.GetReindexJobByIdAsync(job.Id, _cancellationToken).ReturnsForAnyArgs(new ReindexJobWrapper(job, _weakETag));

            // setup search result
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                true,
                Arg.Any<CancellationToken>(),
                true).
                Returns(
                    new SearchResult(0, new List<Tuple<string, string>>()), // First two calls check how many resources need to be reindexed
                    new SearchResult(0, new List<Tuple<string, string>>()));

            await _reindexJobTaskFactory().ExecuteAsync(job, _weakETag, _cancellationToken);

            Assert.Equal(OperationStatus.Completed, job.Status);
            Assert.Equal(0, job.Count);
            Assert.Contains("Appointment", job.ResourceList);
            Assert.Contains("AppointmentResponse", job.ResourceList);
            Assert.Contains("http://hl7.org/fhir/SearchParameter/AppointmentResponse-appointment", job.SearchParamList);
            Assert.Contains(appointmentBaseTypeUri, job.SearchParamList);

            Assert.Empty(job.QueryList);

            await _reindexUtilities.Received().UpdateSearchParameterStatus(
                Arg.Is<IReadOnlyCollection<string>>(r => r.Any(s => s.Contains("Appointment")) && r.Any(s => s.Contains("AppointmentResponse"))),
                Arg.Any<CancellationToken>());
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
            return searchResult;
        }

        private ReindexJobRecord CreateReindexJobRecord(uint maxResourcePerQuery = 100, IReadOnlyDictionary<string, string> paramHashMap = null, List<string> searchParameterTypes = null, List<string> resourceTypes = null)
        {
            if (paramHashMap == null)
            {
                paramHashMap = new Dictionary<string, string>() { { "Patient", "patientHash" } };
            }

            if (searchParameterTypes == null)
            {
                searchParameterTypes = new List<string>();
            }

            if (resourceTypes == null)
            {
                resourceTypes = new List<string>();
            }

            return new ReindexJobRecord(paramHashMap, new List<string>(), searchParameterTypes, searchParameterResourceTypes: resourceTypes, maxiumumConcurrency: 1, maxResourcePerQuery);
        }

        private async Task SetSearchParameterStatus(IReadOnlyCollection<string> searchParameter, SearchParameterStatus status)
        {
            await _searchParameterStatusmanager.UpdateSearchParameterStatusAsync(searchParameter, status, default);
        }
    }
}
