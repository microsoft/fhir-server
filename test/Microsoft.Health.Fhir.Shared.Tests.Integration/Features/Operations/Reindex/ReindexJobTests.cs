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
using Hl7.FhirPath;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
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
    [CollectionDefinition("ReindexJobTests", DisableParallelization = true)]
    public class ReindexJobTests : IClassFixture<FhirStorageTestsFixture>, IAsyncLifetime
    {
        private JobHosting _jobHosting;
        private CancellationTokenSource _jobHostingCts;
        private Task _jobHostingTask;
        private IQueueClient _queueClient;
        private IJobFactory _jobFactory;

        private readonly FhirStorageTestsFixture _fixture;
        private readonly IFhirStorageTestHelper _testHelper;
        private IFhirOperationDataStore _fhirOperationDataStore;
        private IScoped<IFhirDataStore> _scopedDataStore;
        private IFhirStorageTestHelper _fhirStorageTestHelper;
        private IResourceWrapperFactory _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        private SearchParameterDefinitionManager _searchParameterDefinitionManager;
        private SearchParameterDefinitionManager _searchParameterDefinitionManager2;

        private ReindexJobConfiguration _jobConfiguration;
        private CreateReindexRequestHandler _createReindexRequestHandler;
        private ReindexSingleResourceRequestHandler _reindexSingleResourceRequestHandler;
        private readonly ISearchIndexer _searchIndexer = Substitute.For<ISearchIndexer>();
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager;
        private SearchParameterStatusManager _searchParameterStatusManager;
        private ISupportedSearchParameterDefinitionManager _supportedSearchParameterDefinitionManager2;
        private SearchParameterStatusManager _searchParameterStatusManager2;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();

        private readonly ITestOutputHelper _output;
        private IScoped<ISearchService> _searchService;

        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private ISearchParameterOperations _searchParameterOperations = null;
        private ISearchParameterOperations _searchParameterOperations2 = null;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator = Substitute.For<IDataStoreSearchParameterValidator>();
        private IOptions<ReindexJobConfiguration> _optionsReindexConfig = Substitute.For<IOptions<ReindexJobConfiguration>>();

        public ReindexJobTests(FhirStorageTestsFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _testHelper = _fixture.TestHelper;
            _output = output;
        }

        public async Task InitializeAsync()
        {
            // Initialize critical fields first before cleanup
            _fhirOperationDataStore = _fixture.OperationDataStore;
            _fhirStorageTestHelper = _fixture.TestHelper;
            _scopedDataStore = _fixture.DataStore.CreateMockScope();
            _searchService = _fixture.SearchService.CreateMockScope();

            // Now we can safely delete leftover resources from previous test runs
            var cleanupCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await DeleteTestResources(cleanupCts.Token);
            cleanupCts.Dispose();

            _dataStoreSearchParameterValidator.ValidateSearchParameter(default, out Arg.Any<string>()).ReturnsForAnyArgs(x =>
            {
                x[1] = null;
                return true;
            });

            _searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            _jobConfiguration = new ReindexJobConfiguration();
            _optionsReindexConfig.Value.Returns(_jobConfiguration);

            _searchParameterDefinitionManager = _fixture.SearchParameterDefinitionManager;
            _supportedSearchParameterDefinitionManager = _fixture.SupportedSearchParameterDefinitionManager;

            _resourceWrapperFactory = Mock.TypeWithArguments<ResourceWrapperFactory>(
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
                                                _optionsReindexConfig,
                                                _searchParameterDefinitionManager,
                                                _searchParameterOperations);

            _reindexSingleResourceRequestHandler = new ReindexSingleResourceRequestHandler(
                                                    DisabledFhirAuthorizationService.Instance,
                                                    _scopedDataStore.Value,
                                                    _searchIndexer,
                                                    Deserializers.ResourceDeserializer,
                                                    _searchParameterOperations,
                                                    _searchParameterDefinitionManager);

            await _fhirStorageTestHelper.DeleteAllReindexJobRecordsAsync(CancellationToken.None);

            // Initialize second FHIR service
            await InitializeSecondFHIRService();

            await InitializeJobHosting();
        }

        public async Task DisposeAsync()
        {
            // Clean up resources before finishing test class
            await DeleteTestResources();

            await StopJobHostingBackgroundServiceAsync();

            return;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        private async Task InitializeJobHosting()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            // Get the actual queue client from the operation datastore implementation
            var operationDataStoreBase = _fhirOperationDataStore as FhirOperationDataStoreBase;
            if (operationDataStoreBase != null)
            {
                // We need to access the _queueClient field using reflection since it's private
                var field = typeof(FhirOperationDataStoreBase).GetField("_queueClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _queueClient = field?.GetValue(operationDataStoreBase) as IQueueClient;
            }

            // If we couldn't get the actual queue client, use the one from the fixture
            if (_queueClient == null)
            {
                _queueClient = _fixture.QueueClient;
            }

            if (_jobFactory == null)
            {
                // Create a more sophisticated mock job factory that handles reindex jobs
                _jobFactory = Substitute.For<IJobFactory>();
                _jobFactory.Create(Arg.Any<JobInfo>()).Returns(info =>
                {
                    var jobInfo = info.ArgAt<JobInfo>(0);

                    // Deserialize the job definition to get the TypeId
                    int typeId = 0;
                    try
                    {
                        var baseJob = JsonConvert.DeserializeObject<ReindexJobRecord>(jobInfo.Definition);
                        typeId = baseJob.TypeId;
                    }
                    catch
                    {
                        // If not a ReindexJobRecord, try ReindexProcessingJobDefinition
                        var processingJob = JsonConvert.DeserializeObject<ReindexProcessingJobDefinition>(jobInfo.Definition);
                        typeId = processingJob.TypeId;
                    }

                    IJob job = null;

                    if (typeId == (int)JobType.ReindexOrchestrator)
                    {
                        // Create a mock CoreFeatureConfiguration for the test
                        var coreFeatureConfig = Substitute.For<IOptions<CoreFeatureConfiguration>>();
                        coreFeatureConfig.Value.Returns(new CoreFeatureConfiguration
                        {
                            SearchParameterCacheRefreshIntervalSeconds = 1, // Use a short interval for tests
                        });

                        // Create a mock OperationsConfiguration for the test
                        var operationsConfig = Substitute.For<IOptions<OperationsConfiguration>>();
                        operationsConfig.Value.Returns(new OperationsConfiguration
                        {
                            Reindex = new ReindexJobConfiguration
                            {
                                ReindexDelayMultiplier = 1, // Use a short multiplier for tests
                            },
                        });

                        job = new ReindexOrchestratorJob(
                            _queueClient,
                            () => _searchService,
                            _searchParameterDefinitionManager,
                            ModelInfoProvider.Instance,
                            _searchParameterStatusManager,
                            _searchParameterOperations,
                            _fixture.FhirRuntimeConfiguration,
                            NullLoggerFactory.Instance,
                            coreFeatureConfig,
                            operationsConfig);
                    }
                    else if (typeId == (int)JobType.ReindexProcessing)
                    {
                        Func<Health.Extensions.DependencyInjection.IScoped<IFhirDataStore>> fhirDataStoreScope = () => _scopedDataStore.Value.CreateMockScope();
                        job = new ReindexProcessingJob(
                            () => _searchService,
                            fhirDataStoreScope,
                            _resourceWrapperFactory,
                            _searchParameterOperations,
                            NullLogger<ReindexProcessingJob>.Instance);
                    }
                    else
                    {
                        // Fallback to a default mock
                        job = Substitute.For<IJob>();
                        job.ExecuteAsync(Arg.Any<JobInfo>(), Arg.Any<CancellationToken>())
                            .Returns(Task.FromResult("Success"));
                    }

                    var scopedJob = Substitute.For<IScoped<IJob>>();
                    scopedJob.Value.Returns(job);
                    return scopedJob;
                });
            }

            var logger = NullLogger<JobHosting>.Instance;

            _jobHosting = new JobHosting(_queueClient, _jobFactory, logger);

            // Configure for faster polling to make tests run quicker
            _jobHosting.PollingFrequencyInSeconds = 1;
            _jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 30;
            _jobHosting.JobHeartbeatIntervalInSeconds = 5;

            _jobHostingCts = new CancellationTokenSource();

            // Run this on a separate thread to avoid blocking the test
            _jobHostingTask = Task.Run(() => _jobHosting.ExecuteAsync(
                (byte)QueueType.Reindex,  // Use the correct queue type
                runningJobCount: 5,
                workerName: "ReindexTestWorker",
                cancellationTokenSource: _jobHostingCts));
        }

        private async Task StopJobHostingBackgroundServiceAsync()
        {
            if (_jobHostingCts != null)
            {
                _jobHostingCts.Cancel();

                if (_jobHostingTask != null)
                {
                    try
                    {
                        await _jobHostingTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when cancellation occurs
                    }
                }

                _jobHostingCts.Dispose();
                _jobHostingCts = null;
            }
        }

        [Fact]
        public async Task GivenReindexJobQueuedWithBackgroundService_WhenJobCompleted_ThenStatusIsUpdated()
        {
            await CancelActiveReindexJobIfExists();

            // Create a test search parameter
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";
            SearchParameter searchParam = await CreateSearchParam(
                searchParamName,
                SearchParamType.String,
                KnownResourceTypes.Patient,
                "Patient.name",
                searchParamCode);

            // Create a reindex job
            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.NotNull(response.Job);

            string jobId = response.Job.JobRecord.Id;

            // Wait for the job to be processed by our background service
            var timeout = TimeSpan.FromSeconds(240);
            var sw = Stopwatch.StartNew();

            // Poll until job status changes or timeout
            while (sw.Elapsed < timeout)
            {
                // Check job status
                var job = await _fhirOperationDataStore.GetReindexJobByIdAsync(jobId, CancellationToken.None);

                _output.WriteLine($"Job status: {job.JobRecord.Status}, Elapsed time: {sw.Elapsed}");

                if (job.JobRecord.Status == OperationStatus.Completed ||
                    job.JobRecord.Status == OperationStatus.Failed ||
                    job.JobRecord.Status == OperationStatus.Canceled)
                {
                    // Job processing completed
                    break;
                }

                await Task.Delay(1000);
            }

            // Final verification of job status
            var finalJob = await _fhirOperationDataStore.GetReindexJobByIdAsync(jobId, CancellationToken.None);
            Assert.Equal(OperationStatus.Completed, finalJob.JobRecord.Status);
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
            await CancelActiveReindexJobIfExists();

            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));
        }

        [Theory]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MaxMaximumNumberOfResourcesPerQuery + 1)]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MinMaximumNumberOfResourcesPerQuery - 1)]
        [InlineData("Foo", 4)]
        public async Task GivenOutOfRangeReindexParameter_WhenCreatingAReindexJob_ThenExceptionShouldBeThrown(string jobRecordProperty, int value)
        {
            string errorMessage = "Test error message";
            try
            {
                await CancelActiveReindexJobIfExists();

                CreateReindexRequest request;
                switch (jobRecordProperty)
                {
                    case JobRecordProperties.MaximumNumberOfResourcesPerQuery:
                        request = new CreateReindexRequest(new List<string>(), new List<string>(), null, null, value);
                        errorMessage = string.Format(Fhir.Core.Resources.InvalidReIndexParameterValue, jobRecordProperty, ReindexJobRecord.MinMaximumNumberOfResourcesPerQuery, ReindexJobRecord.MaxMaximumNumberOfResourcesPerQuery);
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
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MaxMaximumNumberOfResourcesPerQuery)]
        [InlineData(JobRecordProperties.MaximumNumberOfResourcesPerQuery, ReindexJobRecord.MinMaximumNumberOfResourcesPerQuery)]
        [InlineData("Patient", 4)]
        public async Task GivenValidReindexParameter_WhenCreatingAReindexJob_ThenNewJobShouldBeCreated(string jobRecordProperty, int value)
        {
            await CancelActiveReindexJobIfExists();

            CreateReindexRequest request;
            switch (jobRecordProperty)
            {
                case JobRecordProperties.MaximumNumberOfResourcesPerQuery:
                    request = new CreateReindexRequest(new List<string>(), new List<string>(), null, null, value);
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
        public async Task GivenSingleResourceReindex_ThenReindexJobShouldComplete()
        {
            string observationId = Guid.NewGuid().ToString();
            UpsertOutcome observationSample = await CreateObservationResource(observationId);
            var request = GetReindexRequest("POST", observationId, "Observation");

            ReindexSingleResourceResponse response = await _reindexSingleResourceRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
        }

        private ReindexSingleResourceRequest GetReindexRequest(string httpMethod, string resourceId = null, string resourceType = null)
        {
            resourceId = resourceId ?? Guid.NewGuid().ToString();
            resourceType = resourceType ?? "Observation";

            return new ReindexSingleResourceRequest(httpMethod, resourceType, resourceId);
        }

        [Fact]
        public async Task GivenSearchParametersToReindex_ThenReindexJobShouldComplete()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, KnownResourceTypes.Patient, "Patient.name", searchParamCode);

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

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenAlreadyRunningJob_WhenCreatingAReindexJob_ThenActiveJobShouldBeReturned()
        {
            await CancelActiveReindexJobIfExists();

            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse firstResponse = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(firstResponse);
            Assert.False(string.IsNullOrWhiteSpace(firstResponse.Job.JobRecord.Id));

            // Store the original job ID and status
            string originalJobId = firstResponse.Job.JobRecord.Id;
            OperationStatus originalStatus = firstResponse.Job.JobRecord.Status;

            // Create another reindex request - should return the same job instead of throwing an exception
            CreateReindexResponse secondResponse = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(secondResponse);
            Assert.Equal(originalJobId, secondResponse.Job.JobRecord.Id);
        }

        [Fact]
        public async Task GivenNoSupportedSearchParameters_WhenRunningReindexJob_ThenJobIsCompleted()
        {
            var request = new CreateReindexRequest(new List<string>(), new List<string>());
            CreateReindexResponse response = await SetUpForReindexing(request);

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
               await PerformReindexingOperation(response, OperationStatus.Completed, cancellationTokenSource);
            }
            finally
            {
                cancellationTokenSource.Cancel();
            }
        }

        [Fact]
        public async Task GivenNoMatchingResources_WhenRunningReindexJob_ThenJobIsCompleted()
        {
#if Stu3 || R4 || R4B
            var searchParam = _supportedSearchParameterDefinitionManager.GetSearchParameter("http://hl7.org/fhir/SearchParameter/Measure-name");
#else
            var searchParam = _supportedSearchParameterDefinitionManager.GetSearchParameter("http://hl7.org/fhir/SearchParameter/CanonicalResource-name");
#endif
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
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, KnownResourceTypes.Patient, "Patient.name", searchParamCode);

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

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenReindexJobRunning_WhenReindexJobCancelRequest_ThenReindexJobStopsAndMarkedCanceled()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";
            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, KnownResourceTypes.Patient, "Patient.name", searchParamCode);

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

            var createReindexRequest = new CreateReindexRequest(new List<string>(), new List<string>(), 1, 1);
            CreateReindexResponse response = await SetUpForReindexing(createReindexRequest);

            using var cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var cancelReindexHandler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);
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

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample3.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample4.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
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

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, KnownResourceTypes.Patient, "Patient.name", searchParamCode);

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

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
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

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, KnownResourceTypes.Patient, "Patient.name", searchParamCode);

            var searchParamWrapper = CreateSearchParamResourceWrapper(searchParam);

            await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(searchParamWrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);

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

                await _fixture.DataStore.HardDeleteAsync(sample1.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sample2.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(searchParamWrapper.ToResourceKey(), false, false, CancellationToken.None);
            }
        }

        [Fact]
        public async Task GivenSecondFHIRServiceSynced_WhenSyncParametersOccursDuringDelete_ThenSecondServiceHandlesMissingResourceCorrectly()
        {
            var randomName = Guid.NewGuid().ToString().ComputeHash().Substring(0, 14).ToLower();
            string searchParamName = randomName;
            string searchParamCode = randomName + "Code";

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.String, KnownResourceTypes.Patient, "Patient.name", searchParamCode);

            var searchParamWrapper = CreateSearchParamResourceWrapper(searchParam);

            await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(searchParamWrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);

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

                UpsertOutcome deleteResult = await _fixture.DataStore.UpsertAsync(new ResourceWrapperOperation(deletedWrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);

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

                await _fixture.DataStore.HardDeleteAsync(searchParamWrapper.ToResourceKey(), false, false, CancellationToken.None);
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

            SearchParameter searchParam = await CreateSearchParam(searchParamName, SearchParamType.Token, KnownResourceTypes.Resource, "Resource.id", searchParamCode);

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

                // CRITICAL: Force the search parameter definition manager to refresh/sync
                // This is the missing piece - the search service needs to know about status changes
                await _searchParameterOperations.GetAndApplySearchParameterUpdates(CancellationToken.None);

                // Now test the actual search functionality
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

                await _fixture.DataStore.HardDeleteAsync(samplePatient.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
                await _fixture.DataStore.HardDeleteAsync(sampleObservation.Wrapper.ToResourceKey(), false, false, CancellationToken.None);
            }
        }

        private async Task<ReindexJobWrapper> PerformReindexingOperation(
            CreateReindexResponse response,
            OperationStatus operationStatus,
            CancellationTokenSource cancellationTokenSource,
            int delay = 1000)
        {
            const int MaxNumberOfAttempts = 120;

            ReindexJobWrapper reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

            int delayCount = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();
            while (reindexJobWrapper.JobRecord.Status != operationStatus && delayCount < MaxNumberOfAttempts)
            {
                // Check for any processing jobs that need to be executed
                var allJobs = await _queueClient.GetJobByGroupIdAsync((byte)QueueType.Reindex, response.Job.JobRecord.GroupId, true, cancellationTokenSource.Token);
                var processingJobs = allJobs.Where(j => j.Status == JobStatus.Created || j.Status == JobStatus.Running).ToList();

                // Execute any pending processing jobs
                foreach (var processingJob in processingJobs.Where(j => j.Status == JobStatus.Created))
                {
                    try
                    {
                        var scopedJob = _jobFactory.Create(processingJob);
                        var result = await scopedJob.Value.ExecuteAsync(processingJob, cancellationTokenSource.Token);
                        processingJob.Status = JobStatus.Completed;
                        processingJob.Result = result;
                        await _queueClient.CompleteJobAsync(processingJob, false, cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        processingJob.Status = JobStatus.Failed;
                        processingJob.Result = JsonConvert.SerializeObject(new { Error = ex.Message });
                        await _queueClient.CompleteJobAsync(processingJob, false, cancellationTokenSource.Token);
                    }
                }

                await Task.Delay(delay);
                delayCount++;
                reindexJobWrapper = await _fhirOperationDataStore.GetReindexJobByIdAsync(response.Job.JobRecord.Id, cancellationTokenSource.Token);

                if (operationStatus == OperationStatus.Completed &&
                    (reindexJobWrapper.JobRecord.Status == OperationStatus.Failed || reindexJobWrapper.JobRecord.Status == OperationStatus.Canceled))
                {
                    Assert.Fail($"Fail-fast. Current job status '{reindexJobWrapper.JobRecord.Status}'. Expected job status '{operationStatus}'. Number of attempts: {MaxNumberOfAttempts}. Time elapsed: {stopwatch.Elapsed}.");
                }
            }

            // If we maxed out attempts and did not reach the expected status, clean up any active jobs
            if (reindexJobWrapper.JobRecord.Status != operationStatus)
            {
                Assert.True(
                    operationStatus == reindexJobWrapper.JobRecord.Status,
                    $"Current job status '{reindexJobWrapper.JobRecord.Status}'. Expected job status '{operationStatus}'. Number of attempts: {delayCount}. Time elapsed: {stopwatch.Elapsed}.");
            }

            var serializer = new FhirJsonSerializer();
            _output.WriteLine(serializer.SerializeToString(reindexJobWrapper.ToParametersResourceElement().ToPoco<Parameters>()));

            return reindexJobWrapper;
        }

        private async Task<CreateReindexResponse> SetUpForReindexing(CreateReindexRequest request = null)
        {
            await CancelActiveReindexJobIfExists();

            if (request == null)
            {
                request = new CreateReindexRequest(new List<string>(), new List<string>());
            }

            CreateReindexResponse response = await _createReindexRequestHandler.Handle(request, CancellationToken.None);

            Assert.NotNull(response);
            Assert.False(string.IsNullOrWhiteSpace(response.Job.JobRecord.Id));

            return response;
        }

        private void MockSearchIndexExtraction(IEnumerable<(string id, ISearchValue searchValue)> searchValues, SearchParameter searchParam)
        {
            SearchParameterInfo searchParamInfo = searchParam.ToInfo();

            foreach ((string id, ISearchValue searchValue) in searchValues)
            {
                var searchIndexValues = new List<SearchIndexEntry>();
                searchIndexValues.Add(new SearchIndexEntry(searchParamInfo, searchValue));

                // Add null check for ResourceElement
                _searchIndexer.Extract(Arg.Is<ResourceElement>(r => r != null && r.Id != null && r.Id.Equals(id))).Returns(searchIndexValues);
            }
        }

        private async Task<SearchParameter> CreateSearchParam(string searchParamName, SearchParamType searchParamType, string baseType, string expression, string searchParamCode)
        {
            var searchParam = new SearchParameter
            {
                Url = $"http://hl7.org/fhir/SearchParameter/{baseType}-{searchParamName}",
                Type = searchParamType,
                Expression = expression,
                Name = searchParamName,
                Code = searchParamCode,
                Id = searchParamName,
            };

#if Stu3 || R4 || R4B
            searchParam.Base = new List<ResourceType?>() { Enum.Parse<ResourceType>(baseType) };
#else
            searchParam.Base = new List<VersionIndependentResourceTypesAll?>() { Enum.Parse<VersionIndependentResourceTypesAll>(baseType) };
#endif

            await _searchParameterOperations.AddSearchParameterAsync(searchParam.ToTypedElement(), CancellationToken.None);

            return searchParam;
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

            switch (ModelInfoProvider.Instance.Version)
            {
                case FhirSpecification.Stu3:
                    searchParamInfo = new SearchParameterInfo("url", "url", ValueSets.SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/SearchParameter-url"));
                    break;

                case FhirSpecification.R5:
                    searchParamInfo = new SearchParameterInfo("url", "url", ValueSets.SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/CanonicalResource-url"));
                    break;

                default:
                    searchParamInfo = new SearchParameterInfo("url", "url", ValueSets.SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/conformance-url"));
                    break;
            }

            var searchParamValue = new UriSearchValue(searchParam.Url, false);
            List<SearchIndexEntry> searchIndices = new List<SearchIndexEntry>() { new SearchIndexEntry(searchParamInfo, searchParamValue) };

            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, deleted, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("SearchParameter"));
            wrapper.SearchParameterHash = "hash";

            return wrapper;
        }

        private async Task<UpsertOutcome> CreatePatientResource(string patientName, string patientId)
        {
            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(CreatePatientResourceWrapper(patientName, patientId), true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
        }

        private async Task<UpsertOutcome> CreateObservationResource(string observationId)
        {
            return await _scopedDataStore.Value.UpsertAsync(new ResourceWrapperOperation(CreateObservationResourceWrapper(observationId), true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
        }

        private async Task InitializeSecondFHIRService()
        {
            var collection = new ServiceCollection();
            ServiceProvider services = collection.BuildServiceProvider();

            var mediator = new Mediator(services);
            var searchParameterComparer = Substitute.For<ISearchParameterComparer<SearchParameterInfo>>();
            var statusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            var fhirDataStore = Substitute.For<IFhirDataStore>();

            _searchParameterDefinitionManager2 = new SearchParameterDefinitionManager(
                ModelInfoProvider.Instance,
                mediator,
                _searchService.CreateMockScopeProviderFromScoped(),
                searchParameterComparer,
                statusDataStore.CreateMockScopeProvider(),
                fhirDataStore.CreateMockScopeProvider(),
                NullLogger<SearchParameterDefinitionManager>.Instance);
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

        private async Task CancelActiveReindexJobIfExists(CancellationToken cancellationToken = default)
        {
            var (found, id) = await _fhirOperationDataStore.CheckActiveReindexJobsAsync(cancellationToken);
            if (found && !string.IsNullOrEmpty(id))
            {
                var cancelReindexHandler = new CancelReindexRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance);
                await cancelReindexHandler.Handle(new CancelReindexRequest(id), cancellationToken);

                // Optionally, wait for the job to be marked as canceled
                var job = await _fhirOperationDataStore.GetReindexJobByIdAsync(id, cancellationToken);
                int attempts = 0;
                while (job.JobRecord.Status != OperationStatus.Canceled && attempts < 5)
                {
                    await Task.Delay(500, cancellationToken);
                    job = await _fhirOperationDataStore.GetReindexJobByIdAsync(id, cancellationToken);
                    attempts++;
                }
            }
        }

        private async Task DeleteTestResources(CancellationToken cancellationToken = default)
        {
            try
            {
                _output.WriteLine("Starting DeleteTestResources cleanup");

                // 1. Cancel any active reindex jobs
                await CancelActiveReindexJobIfExists(cancellationToken);

                // 2. Delete all reindex job records from the database
                await _fhirStorageTestHelper.DeleteAllReindexJobRecordsAsync(cancellationToken);
                _output.WriteLine("Deleted all reindex job records");

                // 3. Clean up patient and observation resources using queries
                try
                {
                    // Get all patients created by test
                    var patientResults = await _searchService.Value.SearchAsync("Patient", new List<Tuple<string, string>>(), cancellationToken);
                    foreach (var result in patientResults.Results)
                    {
                        await _fixture.DataStore.HardDeleteAsync(result.Resource.ToResourceKey(), false, false, cancellationToken);
                        _output.WriteLine($"Deleted Patient resource: {result.Resource.ResourceId}");
                    }

                    // Get all observations created by test
                    var observationResults = await _searchService.Value.SearchAsync("Observation", new List<Tuple<string, string>>(), cancellationToken);
                    foreach (var result in observationResults.Results)
                    {
                        await _fixture.DataStore.HardDeleteAsync(result.Resource.ToResourceKey(), false, false, cancellationToken);
                        _output.WriteLine($"Deleted Observation resource: {result.Resource.ResourceId}");
                    }

                    // Get all search parameters created by test
                    var searchResults = await _searchService.Value.SearchAsync("SearchParameter", new List<Tuple<string, string>>(), cancellationToken);
                    foreach (var result in searchResults.Results)
                    {
                        // First remove from definition manager (in-memory)
                        ResourceWrapper wrapper = result.Resource;
                        string url = null;

                        // Extract the URL from the search parameter resource
                        try
                        {
                            var rawResource = wrapper.RawResource;

                            // Use the ResourceDeserializer to extract the URL from the raw resource
                            var searchParamResource = Deserializers.ResourceDeserializer.Deserialize(wrapper);
                            var typedElement = searchParamResource;

                            url = typedElement.Scalar<string>("url")?.ToString();

                            if (!string.IsNullOrEmpty(url))
                            {
                                // Delete from definition manager
                                _searchParameterDefinitionManager.DeleteSearchParameter(url);
                                _searchParameterDefinitionManager2?.DeleteSearchParameter(url);

                                // Delete status
                                await _testHelper.DeleteSearchParameterStatusAsync(url, cancellationToken);
                                await _searchParameterStatusManager2?.DeleteSearchParameterStatusAsync(url, cancellationToken);

                                _output.WriteLine($"Deleted SearchParameter definition and status: {url}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _output.WriteLine($"Error processing SearchParameter {wrapper.ResourceId}: {ex.Message}");
                        }

                        // Delete the actual resource
                        await _fixture.DataStore.HardDeleteAsync(wrapper.ToResourceKey(), false, false, cancellationToken);
                        _output.WriteLine($"Deleted SearchParameter resource: {wrapper.ResourceId}");
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Exception during resource cleanup: {ex.Message}");
                }

                _output.WriteLine("Completed DeleteTestResources cleanup");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error in DeleteTestResources: {ex.Message}");
            }
        }
    }
}
