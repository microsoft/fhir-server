// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
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
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Reindex
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class ReindexJobTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IScoped<IFhirOperationDataStore> _scopedOperationDataStore;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager2;

        private ReindexJobConfiguration _jobConfiguration;
        private CreateReindexRequestHandler _createReindexRequestHandler;
        private ReindexUtilities _reindexUtilities;
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchParameterStatusManager _searchParameterStatusManager;
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager2;
        private SearchParameterStatusManager _searchParameterStatusManager2;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();

        private readonly ITestOutputHelper _output;
        private ReindexJobWorker _reindexJobWorker;
        private IScoped<ISearchService> _searchService;

        private readonly IReindexJobThrottleController _throttleController = Substitute.For<IReindexJobThrottleController>();
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private ISearchParameterOperations _searchParameterOperations = null;
        private ISearchParameterOperations _searchParameterOperations2 = null;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator = Substitute.For<IDataStoreSearchParameterValidator>();

        public ReindexJobTests(FhirStorageTestsFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _testHelper = _fixture.TestHelper;
            _output = output;
        }

        public async Task InitializeAsync()
        {
            _dataStoreSearchParameterValidator.ValidateSearchParameter(default, out Arg.Any<string>()).ReturnsForAnyArgs(x =>
            {
                x[1] = null;
                return true;
            });

            _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _fhirOperationDataStore = _fixture.OperationDataStore;
            _fhirStorageTestHelper = _fixture.TestHelper;
            _scopedOperationDataStore = _fhirOperationDataStore.CreateMockScope();
            _scopedDataStore = _fixture.DataStore.CreateMockScope();

            _jobConfiguration = new ReindexJobConfiguration();
            IOptions<ReindexJobConfiguration> optionsReindexConfig = Substitute.For<IOptions<ReindexJobConfiguration>>();
            optionsReindexConfig.Value.Returns(_jobConfiguration);

            _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
            _supportedSearchParameterDefinitionManager = _fixture.SupportedSearchParameterDefinitionManager;

            ResourceWrapperFactory wrapperFactory = Mock.TypeWithArguments<ResourceWrapperFactory>(
                new RawResourceFactory(new FhirJsonSerializer()),
                new FhirRequestContextAccessor(),
                _searchIndexer,
                _searchParameterDefinitionManager,
                Deserializers.ResourceDeserializer);

            _searchParameterStatusManager = _fixture.SearchParameterStatusManager;

            _searchParameterOperations = new SearchParameterOperations(
                _searchParameterStatusManager,
                _searchParameterDefinitionManager,
                ModelInfoProvider.Instance,
                _searchParameterSupportResolver,
                _dataStoreSearchParameterValidator,
                () => _searchService,
                NullLogger<SearchParameterOperations>.Instance);

            _createReindexRequestHandler = new CreateReindexRequestHandler(
                                                _fhirOperationDataStore,
                                                DisabledFhirAuthorizationService.Instance,
                                                optionsReindexConfig,
                                                _searchParameterDefinitionManager,
                                                _searchParameterOperations);

            _reindexUtilities = new ReindexUtilities(
                () => _scopedDataStore,
                _searchIndexer,
                Deserializers.ResourceDeserializer,
                _supportedSearchParameterDefinitionManager,
                _searchParameterStatusManager,
                wrapperFactory);

            _searchService = _fixture.SearchService.CreateMockScope();

            await _fhirStorageTestHelper.DeleteAllReindexJobRecordsAsync(CancellationToken.None);

            _throttleController.GetThrottleBasedDelay().Returns(0);
            _throttleController.GetThrottleBatchSize().Returns(100U);

            // Initialize second FHIR service
            await InitialieSecondFHIRService();
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public Task GivenALegacyReindexJobRecord_WhenGettingJobStatus_ThenJobRecordShouldReturn()
        {
            string legacyJobRecord = Samples.GetJson("LegacyRawReindexJobRecord");
            var reindexJobRecord = JsonConvert.DeserializeObject<ReindexJobRecord>(legacyJobRecord);
            Assert.True(reindexJobRecord.ResourceCounts.Any());
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenLessThanMaximumRunningJobs_WhenCreatingAReindexJob_ThenNewJobShouldBeCreated()
        {
            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));
        }

        [Theory]
        [InlineData(JobRecordProperties.MaximumConcurrency, ReindexJobRecord.MaxMaximumConcurrency + 1)]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MaxMaximumNumberOfResourcesPerQuery + 1)]
        [InlineData(JobRecordProperties.QueryDelayIntervalInMilliseconds, ReindexJobRecord.MaxQueryDelayIntervalInMilliseconds + 1)]
        [InlineData(JobRecordProperties.TargetDataStoreUsagePercentage, ReindexJobRecord.MaxTargetDataStoreUsagePercentage + 1)]
        [InlineData(JobRecordProperties.MaximumConcurrency, ReindexJobRecord.MinMaximumConcurrency - 1)]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MinMaximumNumberOfResourcesPerQuery - 1)]
        [InlineData(JobRecordProperties.QueryDelayIntervalInMilliseconds, ReindexJobRecord.MinQueryDelayIntervalInMilliseconds - 1)]
        [InlineData(JobRecordProperties.TargetDataStoreUsagePercentage, ReindexJobRecord.MinTargetDataStoreUsagePercentage - 1)]
        [InlineData("Foo", 4)]
        public async Task GivenOutOfRangeReindexParameter_WhenCreatingAReindexJob_ThenExceptionShouldBeThrown(string jobRecordProperty, int value)
        {
            string errorMessage = "Test error message";
            try
            {
                CreateReindexRequest request;
                switch (jobRecordProperty)
                {
                    case JobRecordProperties.MaximumConcurrency:
                        request = new CreateReindexRequest(new List<string>(), new List<string>(), (ushort?)value);
                        errorMessage = string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, jobRecordProperty, ReindexJobRecord.MinMaximumConcurrency, ReindexJobRecord.MaxMaximumConcurrency);
                        break;
                    case JobRecordProperties.MaximumNumberOfResourcesPerQuery:
                        request = new CreateReindexRequest(new List<string>(), new List<string>(), null, (uint?)value);
                        errorMessage = string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, jobRecordProperty, ReindexJobRecord.MinMaximumNumberOfResourcesPerQuery, ReindexJobRecord.MaxMaximumNumberOfResourcesPerQuery);
                        break;
                    case JobRecordProperties.QueryDelayIntervalInMilliseconds:
                        request = new CreateReindexRequest(new List<string>(), new List<string>(), null, null, value);
                        errorMessage = string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, jobRecordProperty, ReindexJobRecord.MinQueryDelayIntervalInMilliseconds, ReindexJobRecord.MaxQueryDelayIntervalInMilliseconds);
                        break;
                    case JobRecordProperties.TargetDataStoreUsagePercentage:
                        request = new CreateReindexRequest(new List<string>(), new List<string>(), null, null, null, (ushort?)value);
                        errorMessage = string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, jobRecordProperty, ReindexJobRecord.MinTargetDataStoreUsagePercentage, ReindexJobRecord.MaxTargetDataStoreUsagePercentage);
                        break;
                    default:
                        request = new CreateReindexRequest(new List<string>(), new List<string>());
                        errorMessage = $"Resource type 'Foo' is not supported. (Parameter 'type')";
                        break;
                }

                CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);
            }
            catch (FhirException fhirExp)
            {
                Assert.NotNull(fhirExp);
                Assert.Equal(errorMessage.ToLower(), fhirExp.Message.ToLower());
            }
            catch (ArgumentException exp)
            {
                Assert.Equal(exp.Message.ToLower(), errorMessage.ToLower());
            }
        }

        [Theory]
        [InlineData(JobRecordProperties.MaximumConcurrency, ReindexJobRecord.MaxMaximumConcurrency)]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MaxMaximumNumberOfResourcesPerQuery)]
        [InlineData(JobRecordProperties.QueryDelayIntervalInMilliseconds, ReindexJobRecord.MaxQueryDelayIntervalInMilliseconds)]
        [InlineData(JobRecordProperties.TargetDataStoreUsagePercentage, ReindexJobRecord.MaxTargetDataStoreUsagePercentage)]
        [InlineData(JobRecordProperties.MaximumConcurrency, ReindexJobRecord.MinMaximumConcurrency)]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MinMaximumNumberOfResourcesPerQuery)]
        [InlineData(JobRecordProperties.QueryDelayIntervalInMilliseconds, ReindexJobRecord.MinQueryDelayIntervalInMilliseconds)]
        [InlineData(JobRecordProperties.TargetDataStoreUsagePercentage, ReindexJobRecord.MinTargetDataStoreUsagePercentage)]
        [InlineData("Patient", 4)]
        public async Task GivenValidReindexParameter_WhenCreatingAReindexJob_ThenNewJobShouldBeCreated(string jobRecordProperty, int value)
        {
            CreateReindexRequest request;
            switch (jobRecordProperty)
            {
                case JobRecordProperties.MaximumConcurrency:
                    request = new CreateReindexRequest(new List<string>(), new List<string>(), (ushort?)value);
                    break;
                case JobRecordProperties.MaximumNumberOfResourcesPerQuery:
                    request = new CreateReindexRequest(new List<string>(), new List<string>(), null, (uint?)value);
                    break;
                case JobRecordProperties.QueryDelayIntervalInMilliseconds:
                    request = new CreateReindexRequest(new List<string>(), new List<string>(), null, null, value);
                    break;
                case JobRecordProperties.TargetDataStoreUsagePercentage:
                    request = new CreateReindexRequest(new List<string>(), new List<string>(), null, null, null, (ushort?)value);
                    break;
                default:
                    request = new CreateReindexRequest(new List<string>(), new List<string>());
                    break;
            }

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);
            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));
        }

        [Fact]
        public async Task GivenSearchParametersToReindex_ThenReindexJobShouldComplete()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, ResourceType.Patient, "Patient.name", searchParamCode);

            string sampleName1 = randomName + "searchIndicesPatient1";
            string sampleName2 = randomName + "searchIndicesPatient2";

            string sampleId1 = Guid.NewGuid().ToString();
            string sampleId2 = Guid.NewGuid().ToString();

            // Set up the values that the search index extraction should return during reindexing
            var searchValues = new List<(string, ISearchValue)> { (sampleId1, new StringSearchValue(sampleName1)), (sampleId2, new StringSearchValue(sampleName2)) };
            MockSearchIndexExtraction(searchValues, searchParam);

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1, sampleId1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2, sampleId2);

            SearchIndexEntry si = sample1.Wrapper.SearchIndices.FirstOrDefault();
            var targetSearchParameterTypes = new List<string>() { si.SearchParameter.Url.OriginalString };

            var queryParams = new List<Tuple<string, string>> { new(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            var request = new CreateReindexRequest(new List<string>(), targetSearchParameterTypes);
            CreateReindexResponse response = await SetUpForReindexing(request);
            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var reindexJobWorker = await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                Assert.True(reindexJobWorker.JobRecord.ResourceCounts.Count > 0);
                Assert.True(reindexJobWorker.JobRecord.Progress > 0);
                Assert.Contains(reindexJobWorker.JobRecord.ResourceList, "Patient");
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenAlreadyRunningJob_WhenCreatingAReindexJob_ThenJobConflictExceptionThrown()
        {
            var request = new CreateReindexRequest(new List<string>(), new List<string>());

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            await Assert.ThrowsAsync<JobConflictException>(() => _createReindexRequestHandler.Handle(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenNoSupportedSearchParameters_WhenRunningReindexJob_ThenJobIsCanceled()
        {
            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse response = await SetUpForReindexing(request);

            using var cancellationTokenSource = new CancellationTokenSource();

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
            var searchParam = _supportedSearchParameterDefinitionManager.GetSearchParameter("http://hl7.org/fhir/SearchParameter/Measure-name");
            await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParam.Url.ToString() }, SearchParameterStatus.Supported, default);

            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse response = await SetUpForReindexing(request);

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                var updateSearchParamList = await _searchParameterStatusManager.GetAllSearchParameterStatus(default);
                Assert.Equal(SearchParameterStatus.Enabled, updateSearchParamList.Where(sp => sp.Uri.OriginalString == searchParam.Url.OriginalString).First().Status);
            }
            finally
            {
                cancellationTokenSource.Cancel();
            }
        }

        [Fact]
        public async Task GivenNewSearchParamCreatedBeforeResourcesToBeIndexed_WhenReindexJobCompleted_ThenResourcesAreIndexedAndParamIsSearchable()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, ResourceType.Patient, "Patient.name", searchParamCode);

            string sampleName1 = randomName + "searchIndicesPatient1";
            string sampleName2 = randomName + "searchIndicesPatient2";

            string sampleId1 = Guid.NewGuid().ToString();
            string sampleId2 = Guid.NewGuid().ToString();

            // Set up the values that the search index extraction should return during reindexing
            var searchValues = new List<(string, ISearchValue)> { (sampleId1, new StringSearchValue(sampleName1)), (sampleId2, new StringSearchValue(sampleName2)) };
            MockSearchIndexExtraction(searchValues, searchParam);

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1, sampleId1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2, sampleId2);

            // Create the query <fhirserver>/Patient?foo=searchIndicesPatient1
            var queryParams = new List<Tuple<string, string>> { new(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "foo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // When search parameters aren't recognized, they are ignored
            // Confirm that "foo" is dropped from the query string and all patients are returned
            Assert.Equal(2, searchResults.Results.Count());

            CreateReindexResponse response = await SetUpForReindexing();

            using var cancellationTokenSource = new CancellationTokenSource();

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

                // Confirm that the reindexing operation did not create a new version of the resource
                Assert.Equal("1", searchResults.Results.FirstOrDefault().Resource.Version);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenReindexJobRunning_WhenReindexJobCancelRequest_ThenReindexJobStopsAndMarkedCanceled()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, ResourceType.Patient, "Patient.name", searchParamCode);

            const string sampleName1 = "searchIndicesPatient1";
            const string sampleName2 = "searchIndicesPatient2";
            const string sampleName3 = "searchIndicesPatient3";
            const string sampleName4 = "searchIndicesPatient4";

            string sampleId1 = Guid.NewGuid().ToString();
            string sampleId2 = Guid.NewGuid().ToString();
            string sampleId3 = Guid.NewGuid().ToString();
            string sampleId4 = Guid.NewGuid().ToString();

            // Set up the values that the search index extraction should return during reindexing
            var searchValues = new List<(string, ISearchValue)>
            {
                (sampleId1, new StringSearchValue(sampleName1)),
                (sampleId2, new StringSearchValue(sampleName2)),
                (sampleId3, new StringSearchValue(sampleName3)),
                (sampleId4, new StringSearchValue(sampleName4)),
            };

            MockSearchIndexExtraction(searchValues, searchParam);

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1, sampleId1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2, sampleId2);
            UpsertOutcome sample3 = await CreatePatientResource(sampleName3, sampleId3);
            UpsertOutcome sample4 = await CreatePatientResource(sampleName4, sampleId4);

            // Create the query <fhirserver>/Patient?foo=searchIndicesPatient1
            var queryParams = new List<Tuple<string, string>> { new(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "foo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // When search parameters aren't recognized, they are ignored
            // Confirm that "foo" is dropped from the query string and all patients are returned
            Assert.Equal(4, searchResults.Results.Count());

            var createReindexRequest = new CreateReindexRequest(new List<string>(), new List<string>(), 1, 1, 500);
            CreateReindexResponse response = await SetUpForReindexing(createReindexRequest);

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var cancelReindexHandler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);
                Task reindexWorkerTask = _reindexJobWorker.ExecuteAsync(cancellationTokenSource.Token);
                await cancelReindexHandler.Handle(new CancelReindexRequest(response.Job.JobRecord.Id), CancellationToken.None);
                var reindexWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

                Assert.Equal(OperationStatus.Canceled, reindexWrapper.JobRecord.Status);
            }
            catch (RequestNotValidException ex)
            {
                // Despite the settings above of the create reindex request which processes only one resource
                // every 500ms, sometimes when the test runs the reindex job is completed before the
                // the cancellation request is processed.  We will ignore this error
                if (!ex.Message.Contains("in state Completed and cannot be cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    throw;
                }
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample3.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample4.Wrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenNewSearchParamCreatedAfterResourcesToBeIndexed_WhenReindexJobCompleted_ThenResourcesAreIndexedAndParamIsSearchable()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";

            string sampleName1 = randomName + "searchIndicesPatient1";
            string sampleName2 = randomName + "searchIndicesPatient2";

            string sampleId1 = Guid.NewGuid().ToString();
            string sampleId2 = Guid.NewGuid().ToString();

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1, sampleId1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2, sampleId2);

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, ResourceType.Patient, "Patient.name", searchParamCode);

            // Create the query <fhirserver>/Patient?foo=searchIndicesPatient1
            var queryParams = new List<Tuple<string, string>> { new(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "foo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // When search parameters aren't recognized, they are ignored
            // Confirm that "foo" is dropped from the query string and all patients are returned
            Assert.Equal(2, searchResults.Results.Count());

            // Set up the values that the search index extraction should return during reindexing
            var searchValues = new List<(string, ISearchValue)> { (sampleId1, new StringSearchValue(sampleName1)), (sampleId2, new StringSearchValue(sampleName2)) };
            MockSearchIndexExtraction(searchValues, searchParam);

            CreateReindexResponse response = await SetUpForReindexing();

            using var cancellationTokenSource = new CancellationTokenSource();

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

                // Confirm that the reindexing operation did not create a new version of the resource
                Assert.Equal("1", searchResults.Results.FirstOrDefault().Resource.Version);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenSecondFHIRServiceSynced_WhenReindexJobCompleted_ThenSecondServiceHasSyncedEnabledParameter()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";

            string sampleName1 = randomName + "searchIndicesPatient1";
            string sampleName2 = randomName + "searchIndicesPatient2";

            string sampleId1 = Guid.NewGuid().ToString();
            string sampleId2 = Guid.NewGuid().ToString();

            UpsertOutcome sample1 = await CreatePatientResource(sampleName1, sampleId1);
            UpsertOutcome sample2 = await CreatePatientResource(sampleName2, sampleId2);

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, ResourceType.Patient, "Patient.name", searchParamCode);

            var searchParamWrapper = CreateSearchParamResourceWrapper(searchParam);

            await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(searchParamWrapper, true, true, null, false, false, bundleOperationId: null), CancellationToken.None);

            // Create the query <fhirserver>/Patient?foo=searchIndicesPatient1
            var queryParams = new List<Tuple<string, string>> { new(searchParamCode, sampleName1) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "foo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // When search parameters aren't recognized, they are ignored
            // Confirm that "foo" is dropped from the query string and all patients are returned
            Assert.Equal(2, searchResults.Results.Count());

            // Set up the values that the search index extraction should return during reindexing
            var searchValues = new List<(string, ISearchValue)> { (sampleId1, new StringSearchValue(sampleName1)), (sampleId2, new StringSearchValue(sampleName2)) };
            MockSearchIndexExtraction(searchValues, searchParam);

            CreateReindexResponse response = await SetUpForReindexing();

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                var queryParams2 = new List<Tuple<string, string>>();

                // make sure we can search for, and find, the newly created search parameter
                queryParams2.Add(new Tuple<string, string>("url", searchParam.Url));
                var result = await _searchService.Value.SearchAsync(KnownResourceTypes.SearchParameter, queryParams2, CancellationToken.None);
                Assert.NotEmpty(result.Results);

                // Rerun the same search as above
                searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

                // This time, foo should not be dropped from the query string
                Assert.Single(searchResults.Results);

                // The foo search parameter can be used to filter for the first test patient
                ResourceWrapper patient = searchResults.Results.FirstOrDefault().Resource;
                Assert.Contains(sampleName1, patient.RawResource.Data);

                // Confirm that the reindexing operation did not create a new version of the resource
                Assert.Equal("1", searchResults.Results.FirstOrDefault().Resource.Version);

                // second service should not have knowledge of new Searchparameter
                bool tryGetSearchParamResult = _searchParameterDefinitionManager2.TryGetSearchParameter(searchParam.Url, out var searchParamInfo);
                Assert.False(tryGetSearchParamResult);

                await _searchParameterOperations2.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // now we should have sync'd the search parameter
                tryGetSearchParamResult = _searchParameterDefinitionManager2.TryGetSearchParameter(searchParam.Url, out searchParamInfo);
                Assert.True(tryGetSearchParamResult);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _searchParameterStatusManager2.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);
                _searchParameterDefinitionManager2.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(searchParamWrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenSecondFHIRServiceSynced_WhenSyncParametersOccursDuringDelete_ThenSecondServiceHandlesMissingResourceCorrectly()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, ResourceType.Patient, "Patient.name", searchParamCode);

            var searchParamWrapper = CreateSearchParamResourceWrapper(searchParam);

            await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(searchParamWrapper, true, true, null, false, false, bundleOperationId: null), CancellationToken.None);

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var queryParams2 = new List<Tuple<string, string>>();

                // make sure we can search for, and find, the newly created search parameter
                queryParams2.Add(new Tuple<string, string>("url", searchParam.Url));
                var result = await _searchService.Value.SearchAsync(KnownResourceTypes.SearchParameter, queryParams2, CancellationToken.None);
                Assert.NotEmpty(result.Results);

                // first service should have knowledge of new Searchparameter
                bool tryGetSearchParamResult1 = _searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Url, out var searchParamInfo);
                Assert.True(tryGetSearchParamResult1);

                // second service should not have knowledge of new Searchparameter
                bool tryGetSearchParamResult2 = _searchParameterDefinitionManager2.TryGetSearchParameter(searchParam.Url, out searchParamInfo);
                Assert.False(tryGetSearchParamResult2);

                ResourceWrapper deletedWrapper = CreateSearchParamResourceWrapper(searchParam, deleted: true);

                // As per DeleteSearchParameterBehavior.Handle, first step of the delete process would be to delete from the in-memory datastore
                // then delete the search parameter resource from data base
                await _searchParameterOperations2.DeleteSearchParameterAsync(deletedWrapper.RawResource, CancellationToken.None);

                UpsertOutcome deleteResult = await _fixture.DataStore.UpsertAsync(new ResourceWrapperOperation(deletedWrapper, true, true, null, false, false, bundleOperationId: null), CancellationToken.None);

                // After trying to sync the new "supported" status, but finding the resource missing, we should have it listed as PendingDelete
                var tryGetSearchParamResult = _searchParameterDefinitionManager2.TryGetSearchParameter(searchParam.Url, out searchParamInfo);
                Assert.True(tryGetSearchParamResult);

                var statuses = await _searchParameterStatusManager2.GetAllSearchParameterStatus(CancellationToken.None);
                Assert.True(statuses.Where(sp => sp.Uri.OriginalString.Equals(searchParamInfo.Url.OriginalString)).First().Status == SearchParameterStatus.PendingDelete);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(searchParamWrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenNewSearchParamWithResourceBaseType_WhenReindexJobCompleted_ThenAllResourcesAreIndexedAndParamIsSearchable()
        {
            string patientId = Guid.NewGuid().ToString();
            string observationId = Guid.NewGuid().ToString();

            UpsertOutcome samplePatient = await CreatePatientResource("samplePatient", patientId);
            UpsertOutcome sampleObservation = await CreateObservationResource(observationId);

            const string searchParamName = "resourceFoo";
            const string searchParamCode = "resourceFooCode";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.Token, ResourceType.Resource, "Resource.id", searchParamCode);

            // Create the query <fhirserver>/Patient?resourceFooCode=<patientId>
            var queryParams = new List<Tuple<string, string>> { new(searchParamCode, patientId) };
            SearchResult searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);

            // Confirm that the search parameter "resourceFoo" is marked as unsupported
            Assert.Equal(searchParamCode, searchResults.UnsupportedSearchParameters.FirstOrDefault()?.Item1);

            // Set up the values that the search index extraction should return during reindexing
            var searchValues = new List<(string, ISearchValue)> { (patientId, new TokenSearchValue(null, patientId, null)), (observationId, new TokenSearchValue(null, observationId, null)) };
            MockSearchIndexExtraction(searchValues, searchParam);

            CreateReindexResponse response = await SetUpForReindexing();

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);

                // Rerun the same search as above
                searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);
                Assert.Single(searchResults.Results);

                // Confirm that the search parameter "resourceFoo" isn't marked as unsupported
                Assert.DoesNotContain(searchResults.UnsupportedSearchParameters, t => t.Item1 == searchParamCode);

                // Create the query <fhirserver>/Patient?resourceFooCode=<nonexistent-id>
                queryParams = new List<Tuple<string, string>> { new(searchParamCode, "nonexistent-id") };

                // No resources should be returned
                searchResults = await _searchService.Value.SearchAsync("Patient", queryParams, CancellationToken.None);
                Assert.Empty(searchResults.Results);

                // Create the query <fhirserver>/Observation?resourceFooCode=<observationId>
                queryParams = new List<Tuple<string, string>> { new(searchParamCode, observationId) };

                // Check that the new search parameter can be used with a different type of resource
                searchResults = await _searchService.Value.SearchAsync("Observation", queryParams, CancellationToken.None);
                Assert.Single(searchResults.Results);

                // Confirm that the search parameter "resourceFoo" isn't marked as unsupported
                Assert.DoesNotContain(searchResults.UnsupportedSearchParameters, t => t.Item1 == searchParamCode);

                // Create the query <fhirserver>/Observation?resourceFooCode=<nonexistent-id>
                queryParams = new List<Tuple<string, string>> { new(searchParamCode, "nonexistent-id") };

                // No resources should be returned
                searchResults = await _searchService.Value.SearchAsync("Observation", queryParams, CancellationToken.None);
                Assert.Empty(searchResults.Results);
            }
            finally
            {
                cancellationTokenSource.Cancel();

                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam.ToTypedElement());
                await _testHelper.DeleteSearchParameterStatusAsync(searchParam.Url, CancellationToken.None);

                await _fixture.DataStore.HardDeleteAsync(samplePatient.Wrapper.ToResourceKey(), false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sampleObservation.Wrapper.ToResourceKey(), false, CancellationToken.None);
            }
        }

        private async Task<ReindexJobWrapper> PerformReindexingOperation(
            CreateReindexResponse response,
            OperationStatus operationStatus,
            CancellationTokenSource cancellationTokenSource,
            int delay = 1000)
        {
            const int MaxNumberOfAttempts = 120;

            Task reindexWorkerTask = _reindexJobWorker.ExecuteAsync(cancellationTokenSource.Token);
            ReindexJobWrapper reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

            int delayCount = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (reindexJobWrapper.JobRecord.Status != operationStatus && delayCount < MaxNumberOfAttempts)
            {
                await Task.Delay(delay);
                delayCount++;
                reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

                if (operationStatus == OperationStatus.Completed &&
                    (reindexJobWrapper.JobRecord.Status == OperationStatus.Failed || reindexJobWrapper.JobRecord.Status == OperationStatus.Canceled))
                {
                    // Fail-fast.
                    // If the expected status is 'Completed', and the job failed or if it was canceled, then stop the test quickly.
                    Assert.Fail($"Fail-fast. Current job status '{reindexJobWrapper.JobRecord.Status}'. Expected job status '{operationStatus}'. Number of attempts: {MaxNumberOfAttempts}. Time elapsed: {stopwatch.Elapsed}.");
                }
            }

            var serializer = new FhirJsonSerializer();
            _output.WriteLine(serializer.SerializeToString(reindexJobWrapper.ToParametersResourceElement().ToPoco<Parameters>()));

            Assert.True(
                operationStatus == reindexJobWrapper.JobRecord.Status,
                $"Current job status '{reindexJobWrapper.JobRecord.Status}'. Expected job status '{operationStatus}'. Number of attempts: {delayCount}. Time elapsed: {stopwatch.Elapsed}.");

            return reindexJobWrapper;
        }

        private async Task<CreateReindexResponse> SetUpForReindexing(CreateReindexRequest request = null)
        {
            if (request == null)
            {
                request = new CreateReindexRequest(new List<string>(), new List<string>());
            }

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            _reindexJobWorker = new ReindexJobWorker(
                () => _scopedOperationDataStore,
                Options.Create(_jobConfiguration),
                InitializeReindexJobTask,
                _searchParameterOperations,
                NullLogger<ReindexJobWorker>.Instance);

            await _reindexJobWorker.Handle(new SearchParametersInitializedNotification(), CancellationToken.None);

            return response;
        }

        private void MockSearchIndexExtraction(IEnumerable<(string id, ISearchValue searchValue)> searchValues, SearchParameter searchParam)
        {
            SearchParameterInfo searchParamInfo = searchParam.ToInfo();

            foreach ((string id, ISearchValue searchValue) in searchValues)
            {
                var searchIndexValues = new List<SearchIndexEntry>();
                searchIndexValues.Add(new SearchIndexEntry(searchParamInfo, searchValue));
                _searchIndexer.Extract(Arg.Is<ResourceElement>(r => r.Id.Equals(id))).Returns(searchIndexValues);
            }
        }

        private async Task<SearchParameter> CreateSearchParam(string searchParamName, SearchParamType searchParamType, ResourceType baseType, string expression, string searchParamCode)
        {
            var searchParam = new SearchParameter
            {
                Url = $"http://hl7.org/fhir/SearchParameter/{baseType}-{searchParamName}",
                Type = searchParamType,
                Base = new List<ResourceType?> { baseType },
                Expression = expression,
                Name = searchParamName,
                Code = searchParamCode,
                Id = searchParamName,
            };

            await _searchParameterOperations.AddSearchParameterAsync(searchParam.ToTypedElement(), CancellationToken.None);

            return searchParam;
        }

        private ReindexJobTask InitializeReindexJobTask()
        {
            return new ReindexJobTask(
                () => _scopedOperationDataStore,
                () => _scopedDataStore,
                Options.Create(_jobConfiguration),
                () => _searchService,
                _supportedSearchParameterDefinitionManager,
                _reindexUtilities,
                _contextAccessor,
                _throttleController,
                ModelInfoProvider.Instance,
                NullLogger<ReindexJobTask>.Instance,
                _searchParameterStatusManager);
        }

        private ResourceWrapper CreatePatientResourceWrapper(string patientName, string patientId)
        {
            Patient patientResource = Samples.GetDefaultPatient().ToPoco<Patient>();

            patientResource.Name = new List<HumanName> { new() { Family = patientName } };
            patientResource.Id = patientId;
            patientResource.VersionId = "1";

            var resourceElement = patientResource.ToResourceElement();
            var rawResource = new RawResource(patientResource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = new List<SearchIndexEntry>() { new SearchIndexEntry(new SearchParameterInfo("name", "name", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name")) { SortStatus = SortParameterStatus.Enabled }, new StringSearchValue(patientName)) };
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient"));
            wrapper.SearchParameterHash = "hash";

            return wrapper;
        }

        private ResourceWrapper CreateObservationResourceWrapper(string observationId)
        {
            Observation observationResource = Samples.GetDefaultObservation().ToPoco<Observation>();

            observationResource.Id = observationId;
            observationResource.VersionId = "1";

            var resourceElement = observationResource.ToResourceElement();
            var rawResource = new RawResource(observationResource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Put);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var searchIndices = new List<SearchIndexEntry>() { new SearchIndexEntry(new SearchParameterInfo("status", "status", ValueSets.SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Observation-status")) { SortStatus = SortParameterStatus.Disabled }, new StringSearchValue("final")) };
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Observation"));
            wrapper.SearchParameterHash = "hash";

            return wrapper;
        }

        private ResourceWrapper CreateSearchParamResourceWrapper(SearchParameter searchParam, bool deleted = false)
        {
            searchParam.Id = "searchParam1";
            var resourceElement = searchParam.ToResourceElement();
            var rawResource = new RawResource(searchParam.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = new ResourceRequest(WebRequestMethods.Http.Post);
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            SearchParameterInfo searchParamInfo = null;

            if (ModelInfoProvider.Instance.Version == FhirSpecification.Stu3)
            {
                searchParamInfo = new SearchParameterInfo("url", "url", ValueSets.SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/SearchParameter-url"));
            }
            else
            {
                searchParamInfo = new SearchParameterInfo("url", "url", ValueSets.SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/conformance-url"));
            }

            var searchParamValue = new UriSearchValue(searchParam.Url, false);
            List<SearchIndexEntry> searchIndices = new List<SearchIndexEntry>() { new SearchIndexEntry(searchParamInfo, searchParamValue) };

            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, deleted, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("SearchParameter"));
            wrapper.SearchParameterHash = "hash";

            return wrapper;
        }

        private async Task<UpsertOutcome> CreatePatientResource(string patientName, string patientId)
        {
            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(CreatePatientResourceWrapper(patientName, patientId), true, true, null, false, false, bundleOperationId: null), CancellationToken.None);
        }

        private async Task<UpsertOutcome> CreateObservationResource(string observationId)
        {
            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(CreateObservationResourceWrapper(observationId), true, true, null, false, false, bundleOperationId: null), CancellationToken.None);
        }

        private async Task InitialieSecondFHIRService()
        {
            var collection = new ServiceCollection();
            ServiceProvider services = collection.BuildServiceProvider();

            var mediator = new Mediator(services);

            _searchParameterDefinitionManager2 = new SearchParameterDefinitionManager(ModelInfoProvider.Instance, mediator, () => _searchService, NullLogger<SearchParameterDefinitionManager>.Instance);
            await _searchParameterDefinitionManager2.EnsureInitializedAsync(CancellationToken.None);
            _supportedSearchParameterDefinitionManager2 = new SupportedSearchParameterDefinitionManager(_searchParameterDefinitionManager2);

            _searchParameterStatusManager2 = new SearchParameterStatusManager(_fixture.SearchParameterStatusDataStore, _searchParameterDefinitionManager2, _searchParameterSupportResolver, mediator, NullLogger<SearchParameterStatusManager>.Instance);
            await _searchParameterStatusManager2.EnsureInitializedAsync(CancellationToken.None);

            _searchParameterOperations2 = new SearchParameterOperations(
                _searchParameterStatusManager2,
                _searchParameterDefinitionManager2,
                ModelInfoProvider.Instance,
                _searchParameterSupportResolver,
                _dataStoreSearchParameterValidator,
                () => _searchService,
                NullLogger<SearchParameterOperations>.Instance);
        }
    }
}
