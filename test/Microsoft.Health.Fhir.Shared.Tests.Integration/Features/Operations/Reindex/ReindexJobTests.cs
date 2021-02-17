// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.CosmosDb)]
    public class ReindexJobTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;
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
        private SearchParameterStatusManager _searchParameterStatusManager;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
        private readonly IMediator _mediator = Substitute.For<IMediator>();

        private ReindexJobWorker _reindexJobWorker;
        private IScoped<ISearchService> _searchService;

        public ReindexJobTests(FhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _testHelper = _fixture.TestHelper;
            _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));
        }

        public async Task InitializeAsync()
        {
            _fhirOperationDataStore = _fixture.OperationDataStore;
            _fhirStorageTestHelper = _fixture.TestHelper;
            _scopedOperationDataStore = _fhirOperationDataStore.CreateMockScope();
            _scopedDataStore = _fixture.DataStore.CreateMockScope();
            _searchParameterStatusDataStore = _fixture.SearchParameterStatusDataStore;

            _jobConfiguration = new ReindexJobConfiguration();
            IOptions<ReindexJobConfiguration> optionsReindexConfig = Substitute.For<IOptions<ReindexJobConfiguration>>();
            optionsReindexConfig.Value.Returns(_jobConfiguration);

            _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
            _supportedSearchParameterDefinitionManager = _fixture.SupportedSearchParameterDefinitionManager;

            ResourceWrapperFactory wrapperFactory = Mock.TypeWithArguments<ResourceWrapperFactory>(
                new RawResourceFactory(new FhirJsonSerializer()),
                _searchIndexer,
                _searchParameterDefinitionManager,
                Deserializers.ResourceDeserializer);

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
                _searchParameterStatusManager,
                wrapperFactory);

            _searchService = _fixture.SearchService.CreateMockScope();

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
                await PerformReindexingOperation(response, OperationStatus.Canceled, cancellationTokenSource);
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
                await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                Assert.True(searchParam.IsSearchable);
            }
            finally
            {
                cancellationTokenSource.Cancel();
                searchParam.IsSearchable = true;
            }
        }

        [Fact]
        public async Task GivenNewSearchParamCreatedBeforeResourcesToBeIndexed_WhenReindexJobCompleted_ThenResourcesAreIndexedAndParamIsSearchable()
        {
            const string searchParamName = "foo";
            const string searchParamCode = "fooCode";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, searchParamCode);

            const string sampleName1 = "searchIndicesPatient1";
            const string sampleName2 = "searchIndicesPatient2";

            // Set up the values that the search index extraction should return on resource creation
            MockSearchIndexExtraction(sampleName1, sampleName2, searchParam);

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2);

            // Create the query <fhirserver>/Patient?foo=searchIndicesPatient1
            var queryParams = new List<Tuple<string, string>>() { new Tuple<string, string>(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "foo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // When search parameters aren't recognized, they are ignored
            // Confirm that "foo" is dropped from the query string and all patients are returned
            Assert.Equal(2, searchResults.Results.Count());

            CreateReindexResponse response = await SetUpForReindexing();

            var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                // Rerun the same search as above
                searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

                // This time, foo should not be dropped from the query string
                Assert.Single(searchResults.Results);

                // The foo search parameter can be used to filter for the first test patient
                ResourceWrapper patient = searchResults.Results.FirstOrDefault().Resource;
                Assert.Contains(sampleName1, patient.RawResource.Data);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenNewSearchParamCreatedAfterResourcesToBeIndexed_WhenReindexJobCompleted_ThenResourcesAreIndexedAndParamIsSearchable()
        {
            const string sampleName1 = "searchIndicesPatient1";
            const string sampleName2 = "searchIndicesPatient2";

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2);

            const string searchParamName = "foo";
            const string searchParamCode = "fooCode";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, searchParamCode);

            // Create the query <fhirserver>/Patient?foo=searchIndicesPatient1
            var queryParams = new List<Tuple<string, string>>() { new Tuple<string, string>(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "foo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // When search parameters aren't recognized, they are ignored
            // Confirm that "foo" is dropped from the query string and all patients are returned
            Assert.Equal(2, searchResults.Results.Count());

            // Set up the values that the search index extraction should return during reindexing
            MockSearchIndexExtraction(sampleName1, sampleName2, searchParam);

            CreateReindexResponse response = await SetUpForReindexing();

            var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                // Rerun the same search as above
                searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

                // This time, foo should not be dropped from the query string
                Assert.Single(searchResults.Results);

                // The foo search parameter can be used to filter for the first test patient
                ResourceWrapper patient = searchResults.Results.FirstOrDefault().Resource;
                Assert.Contains(sampleName1, patient.RawResource.Data);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), CancellationToken.None);
            }
        }

        private async Task PerformReindexingOperation(CreateReindexResponse response, OperationStatus operationStatus, CancellationTokenSource cancellationTokenSource)
        {
            Task reindexWorkerTask = _reindexJobWorker.ExecuteAsync(cancellationTokenSource.Token);
            ReindexJobWrapper reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

            int delayCount = 0;
            while (reindexJobWrapper.JobRecord.Status != operationStatus && delayCount < 10)
            {
                await Task.Delay(1000);
                delayCount++;
                reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);
            }

            Assert.InRange(delayCount, 0, 9);
        }

        private async Task<CreateReindexResponse> SetUpForReindexing()
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
            return response;
        }

        private void MockSearchIndexExtraction(string sampleName1, string sampleName2, SearchParameter searchParam)
        {
            SearchParameterInfo searchParamInfo = searchParam.ToInfo();

            var searchIndexValues1 = new List<SearchIndexEntry>();
            searchIndexValues1.Add(new SearchIndexEntry(searchParamInfo, new StringSearchValue(sampleName1)));
            _searchIndexer.Extract(Arg.Is<ResourceElement>(r => r.Id.Equals(sampleName1))).Returns(searchIndexValues1);

            var searchIndexValues2 = new List<SearchIndexEntry>();
            searchIndexValues2.Add(new SearchIndexEntry(searchParamInfo, new StringSearchValue(sampleName2)));
            _searchIndexer.Extract(Arg.Is<ResourceElement>(r => r.Id.Equals(sampleName2))).Returns(searchIndexValues2);
        }

        private async Task<SearchParameter> CreateSearchParam(string searchParamName, string searchParamCode)
        {
            var searchParam = new SearchParameter()
            {
                Url = $"http://hl7.org/fhir/SearchParameter/Patient-{searchParamName}",
                Type = SearchParamType.String,
                Base = new List<ResourceType?>() { ResourceType.Patient },
                Expression = "Patient.name",
                Name = searchParamName,
                Code = searchParamCode,
            };

            _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement> { searchParam.ToTypedElement() });

            // Add the search parameter to the datastore
            await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string> { searchParam.Url.ToString() }, SearchParameterStatus.Supported);

            return searchParam;
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
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var resourceElement = Deserializers.ResourceDeserializer.DeserializeRaw(rawResource, "v1", DateTimeOffset.UtcNow);
            var searchIndices = _searchIndexer.Extract(resourceElement);
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));

            return wrapper;
        }

        private async Task<UpsertOutcome> CreatePatientResource(string sampleName)
        {
            return await _scopedDataStore.Value.UpsertAsync(CreateResourceWrapper(sampleName), null, true, true, CancellationToken.None);
        }
    }
}
