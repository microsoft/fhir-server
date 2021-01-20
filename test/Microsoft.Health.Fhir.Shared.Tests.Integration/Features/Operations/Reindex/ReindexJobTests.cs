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
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
    public class ReindexJobTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IScoped<IFhirOperationDataStore> _scopedOperationDataStore;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;

        private ReindexJobConfiguration _jobConfiguration;
        private CreateReindexRequestHandler _createReindexRequestHandler;
        private ReindexUtilities _reindexUtilities;
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchableSearchParameterDefinitionManager _searchableSearchParameterDefinitionManager;
        private readonly IOptions<CoreFeatureConfiguration> coreOptions = Substitute.For<IOptions<CoreFeatureConfiguration>>();
        private SearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ISortingValidator _sortingValidator = new CosmosDbSortingValidator();

        private ReindexJobWorker _reindexJobWorker;
        private IScoped<ISearchService> _searchService;

        public ReindexJobTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));
        }

        public async Task InitializeAsync()
        {
            _fhirOperationDataStore = _fixture.OperationDataStore;
            _fhirStorageTestHelper = _fixture.TestHelper;
            _scopedOperationDataStore = _fixture.OperationDataStore.CreateMockScope();
            _scopedDataStore = _fixture.DataStore.CreateMockScope();
            _searchParameterStatusDataStore = _fixture.SearchParameterStatusDataStore;

            _jobConfiguration = new ReindexJobConfiguration();
            IOptions<ReindexJobConfiguration> optionsReindexConfig = Substitute.For<IOptions<ReindexJobConfiguration>>();
            optionsReindexConfig.Value.Returns(_jobConfiguration);

            _searchParameterDefinitionManager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance);
            await _searchParameterDefinitionManager.StartAsync(CancellationToken.None);
            _supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager);
            var fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            _searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(_searchParameterDefinitionManager, fhirRequestContextAccessor);

            _searchParameterStatusManager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                _searchParameterSupportResolver,
                _mediator);

            _createReindexRequestHandler = new CreateReindexRequestHandler(
                                                _fhirOperationDataStore,
                                                DisabledFhirAuthorizationService.Instance,
                                                optionsReindexConfig,
                                                _searchParameterDefinitionManager);
            _reindexUtilities = new ReindexUtilities(
                () => _scopedDataStore,
                _searchIndexer,
                Deserializers.ResourceDeserializer,
                _supportedSearchParameterDefinitionManager,
                _searchParameterStatusManager);

            coreOptions.Value.Returns(new CoreFeatureConfiguration());

            var searchParameterExpressionParser = new SearchParameterExpressionParser(new ReferenceSearchValueParser(fhirRequestContextAccessor), ModelInfoProvider.Instance);
            var expressionParser = new ExpressionParser(() => _searchableSearchParameterDefinitionManager, searchParameterExpressionParser);
            var searchOptionsFactory = new SearchOptionsFactory(expressionParser, () => _searchableSearchParameterDefinitionManager, coreOptions, fhirRequestContextAccessor, _sortingValidator, NullLogger<SearchOptionsFactory>.Instance);
            var cosmosSearchService = new FhirCosmosSearchService(searchOptionsFactory, _fixture.DataStore as CosmosFhirDataStore, new QueryBuilder(), _searchParameterDefinitionManager, fhirRequestContextAccessor) as ISearchService;

            _searchService = cosmosSearchService.CreateMockScope();

            await _fhirStorageTestHelper.DeleteAllReindexJobRecordsAsync(CancellationToken.None);
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenLessThanMaximumRunningJobs_WhenCreatingAReindexJob_ThenNewJobShouldBeCreated()
        {
            var request = new CreateReindexRequest();

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));
        }

        [Fact]
        public async Task GivenAlreadyRunningJob_WhenCreatingAReindexJob_ThenJobConflictExceptionThrown()
        {
            var request = new CreateReindexRequest();

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            await Assert.ThrowsAsync<JobConflictException>(() => _createReindexRequestHandler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNoSupportedSearchParameters_WhenRunningReindexJob_ThenJobIsCanceled()
        {
            var request = new CreateReindexRequest();

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            _reindexJobWorker = new ReindexJobWorker(
                () => _scopedOperationDataStore,
                Options.Create(_jobConfiguration),
                InitializeReindexJobTask,
                NullLogger<ReindexJobWorker>.Instance);

            var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var reindexWorkerTask = _reindexJobWorker.ExecuteAsync(cancellationTokenSource.Token);
                var reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

                int delayCount = 0;
                while (reindexJobWrapper.JobRecord.Status != OperationStatus.Canceled
                    && delayCount < 10)
                {
                    await Task.Delay(1000);
                    delayCount++;
                    reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);
                }

                Assert.True(delayCount <= 9);
            }
            finally
            {
                cancellationTokenSource.Cancel();
            }
        }

        [Fact]
        public async Task GivenNoMatchingResources_WhenRunningReindexJob_ThenJobIsCompleted()
        {
            var searchParam = _supportedSearchParameterDefinitionManager.GetSearchParameter(new Uri("http://hl7.org/fhir/SearchParameter/Measure-name"));
            searchParam.IsSearchable = false;
            var request = new CreateReindexRequest();

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            _reindexJobWorker = new ReindexJobWorker(
                () => _scopedOperationDataStore,
                Options.Create(_jobConfiguration),
                InitializeReindexJobTask,
                NullLogger<ReindexJobWorker>.Instance);

            var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var reindexWorkerTask = _reindexJobWorker.ExecuteAsync(cancellationTokenSource.Token);
                var reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

                int delayCount = 0;
                while (reindexJobWrapper.JobRecord.Status != OperationStatus.Completed
                    && delayCount < 10)
                {
                    await Task.Delay(1000);
                    delayCount++;
                    reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);
                }

                Assert.True(delayCount <= 9);
            }
            finally
            {
                cancellationTokenSource.Cancel();
                searchParam.IsSearchable = true;
            }
        }

        [Fact]
        public async Task GivenNewSearchParam_WhenReindexJobCompleted_ThenParamIsSearchable()
        {
            var searchParamName = "foo";
            var searchParam = new SearchParameterInfo(
                name: searchParamName,
                searchParamType: ValueSets.SearchParamType.String,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-foo"),
                components: null,
                expression: "Patient.name",
                targetResourceTypes: null,
                baseResourceTypes: new List<string>() { "Patient" })
            {
                IsSupported = true,
                IsSearchable = false,
            };

            _searchParameterDefinitionManager.UrlLookup.Add(searchParam.Url, searchParam);
            _searchParameterDefinitionManager.TypeLookup["Patient"].Add(searchParamName, searchParam);

            await UpsertPatientData("searchIndicesPatient1");
            await UpsertPatientData("searchIndicesPatient2");

            var queryParams = new List<Tuple<string, string>>() { new Tuple<string, string>("foo", "searchIndicesPatient1") };
            var searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            Assert.Equal(searchParamName, searchResults.UnsupportedSearchParameters.FirstOrDefault().Item1);
            Assert.Equal(2, searchResults.Results.Count());

            var searchIndexValues1 = new List<SearchIndexEntry>();
            searchIndexValues1.Add(new SearchIndexEntry(searchParam, new StringSearchValue("searchIndicesPatient1")));
            _searchIndexer.Extract(Arg.Is<ResourceElement>(r => r.Id.Equals("searchIndicesPatient1"))).Returns(searchIndexValues1);

            var searchIndexValues2 = new List<SearchIndexEntry>();
            searchIndexValues2.Add(new SearchIndexEntry(searchParam, new StringSearchValue("searchIndicesPatient2")));
            _searchIndexer.Extract(Arg.Is<ResourceElement>(r => r.Id.Equals("searchIndicesPatient2"))).Returns(searchIndexValues2);

            var request = new CreateReindexRequest();

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            _reindexJobWorker = new ReindexJobWorker(
                () => _scopedOperationDataStore,
                Options.Create(_jobConfiguration),
                InitializeReindexJobTask,
                NullLogger<ReindexJobWorker>.Instance);

            var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var reindexWorkerTask = _reindexJobWorker.ExecuteAsync(cancellationTokenSource.Token);
                var reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

                int delayCount = 0;
                while (reindexJobWrapper.JobRecord.Status != OperationStatus.Completed
                    && delayCount < 10)
                {
                    await Task.Delay(1000);
                    delayCount++;
                    reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);
                }

                Assert.True(delayCount <= 9);

                searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

                Assert.Single(searchResults.Results);

                var patient = searchResults.Results.FirstOrDefault().Resource;
                Assert.Contains("searchIndicesPatient1", patient.RawResource.Data);
            }
            finally
            {
                cancellationTokenSource.Cancel();
            }
        }

        private ReindexJobTask InitializeReindexJobTask()
        {
            return new ReindexJobTask(
                () => _scopedOperationDataStore,
                Options.Create(_jobConfiguration),
                () => _searchService,
                _supportedSearchParameterDefinitionManager,
                _reindexUtilities,
                NullLogger<ReindexJobTask>.Instance);
        }

        private ResourceWrapper CreateResourceWrapper(string patientName)
        {
            var json = Samples.GetJson("Patient");
            json = json.Replace("Chalmers", patientName);
            json = json.Replace("\"id\": \"example\"", "\"id\": \"" + patientName + "\"");
            var rawResource = new RawResource(json, FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = Substitute.For<ResourceRequest>();
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var resourceElement = Deserializers.ResourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");

            return wrapper;
        }

        private async Task UpsertPatientData(string sampleName)
        {
            await _scopedDataStore.Value.UpsertAsync(CreateResourceWrapper(sampleName), null, true, true, CancellationToken.None);
        }
    }
}
