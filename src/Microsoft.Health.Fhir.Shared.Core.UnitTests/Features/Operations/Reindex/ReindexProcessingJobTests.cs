// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Utility;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            IScopeProvider<IFhirDataStore> fhirDataStoreScope = _fhirDataStore.CreateMockScopeProvider();
            _cancellationToken = _cancellationTokenSource.Token;
            _reindexProcessingJobTaskFactory = () =>
                 new ReindexProcessingJob(
                     _searchService.CreateMockScopeProvider(),
                     fhirDataStoreScope,
                     _resourceWrapperFactory,
                     _searchParameterOperations,
                     NullLogger<ReindexProcessingJob>.Instance);

            // Default range discovery mock for SQL path - can be overridden per test
            _searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>())
                .Returns(callInfo => Task.FromResult<IReadOnlyList<(long StartId, long EndId, int Count)>>(new List<(long StartId, long EndId, int Count)>
                {
                    (callInfo.ArgAt<long>(1), callInfo.ArgAt<long>(2), 1),
                }));

            // Default mock for SearchBySurrogateIdRange (SQL path) - can be overridden per test
            _searchService.SearchBySurrogateIdRange(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchResult(
                    new List<SearchResultEntry>(),
                    null,
                    null,
                    new List<Tuple<string, string>>()));
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
                },
                SearchParameterHash = "accountHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

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

        [Fact]
        public async Task GivenReindexedResourcesWithOverlongReferenceIds_WhenExecuted_ThenTheResultCountsThemPerResourceTypeAndParameter()
        {
            var expectedResourceType = "Observation";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex() { Count = 3, EndResourceSurrogateId = 0, StartResourceSurrogateId = 0 },
                SearchParameterHash = "observationHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(job), QueueType = (byte)QueueType.Reindex, GroupId = 1, CreateDate = DateTime.UtcNow, Status = JobStatus.Running };

            _searchService.SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), false, Arg.Any<CancellationToken>(), true)
                .Returns(new SearchResult(Enumerable.Range(1, 3).Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType)).ToList(), null, null, new List<Tuple<string, string>>()));

            var performer = new SearchParameterInfo("performer", "performer", ValueSets.SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-performer"));
            var overlong = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", new string('a', 64)) { OverlongResourceIdLength = 65, MaxResourceIdLength = 64 };
            _resourceWrapperFactory
                .UpdateAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    foreach (ResourceWrapper wrapper in callInfo.Arg<IReadOnlyCollection<ResourceWrapper>>())
                    {
                        wrapper.UpdateSearchIndices(wrapper.ResourceId == "3" ? new List<SearchIndexEntry>() : new List<SearchIndexEntry> { new SearchIndexEntry(performer, overlong) });
                    }

                    return Task.CompletedTask;
                });

            string serialized = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(serialized);

            Assert.Equal(3, result.SucceededResourceCount);
            OverlongReferenceIdCount count = Assert.Single(result.OverlongReferenceIds);
            Assert.Equal(expectedResourceType, count.ResourceType);
            Assert.Equal("performer", count.SearchParameterCode);
            Assert.Equal(64, count.MaxResourceIdLength);
            Assert.Equal(2, count.Count);
        }

        [Fact]
        public async Task GivenReindexedResourcesWithoutOverlongReferenceIds_WhenExecuted_ThenTheResultOmitsTheCounts()
        {
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = "Account",
                ResourceCount = new SearchResultReindex() { Count = 1, EndResourceSurrogateId = 0, StartResourceSurrogateId = 0 },
                SearchParameterHash = "accountHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(job), QueueType = (byte)QueueType.Reindex, GroupId = 1, CreateDate = DateTime.UtcNow, Status = JobStatus.Running };
            _searchService.SearchForReindexAsync(Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<string>(), false, Arg.Any<CancellationToken>(), true)
                .Returns(new SearchResult(new List<SearchResultEntry> { CreateSearchResultEntry("1", "Account") }, null, null, new List<Tuple<string, string>>()));

            string serialized = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);

            Assert.DoesNotContain(JobRecordProperties.OverlongReferenceIds, serialized, StringComparison.Ordinal);
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

        private SearchParameterInfo CreateSearchParameterInfo(string resourceType, string url, bool isSystemDefined = false)
        {
            return new SearchParameterInfo(
                resourceType,
                "name",
                ValueSets.SearchParamType.String,
                url: new Uri(url),
                baseResourceTypes: new List<string> { resourceType })
            {
                IsSearchable = true,
                IsSystemDefined = isSystemDefined,
                SearchParameterStatus = SearchParameterStatus.Supported,
            };
        }

        [Fact]
        public async Task ExecuteAsync_WithNullJobInfo_ThrowsArgumentNullException()
        {
            var job = _reindexProcessingJobTaskFactory();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => job.ExecuteAsync(null, _cancellationToken));
        }

        [Fact]
        public async Task ExecuteAsync_WithValidJobInfo_ReturnsSerializedResult()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 2,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 2)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            // Mock SearchBySurrogateIdRange (SQL path - called when ResourceCount is set)
            _searchService.SearchBySurrogateIdRange(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);

            Assert.NotEmpty(result);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);
            Assert.NotNull(jobResult);
            Assert.Equal(2, jobResult.SucceededResourceCount);
            Assert.Null(jobResult.Warnings);
            Assert.DoesNotContain("Warning", result, StringComparison.Ordinal);
        }

        [Fact]
        public async Task GivenTheDataStoreReturnsWarnings_WhenExecuted_ThenTheJobCompletesAndReportsThem()
        {
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = "Patient",
                ResourceCount = new SearchResultReindex() { Count = 2, StartResourceSurrogateId = 1, EndResourceSurrogateId = 100 },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };
            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(job), QueueType = (byte)QueueType.Reindex, GroupId = 1, CreateDate = DateTime.UtcNow, Status = JobStatus.Running };
            _searchService.SearchBySurrogateIdRange(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(new SearchResult(new[] { CreateSearchResultEntry("1", "Patient"), CreateSearchResultEntry("2", "Patient") }, null, null, new List<Tuple<string, string>>()));
            _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>())
                .Returns(new List<string> { "Resource Patient/1 was not indexed." });

            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            Assert.Equal(2, jobResult.SucceededResourceCount);
            Assert.Null(jobResult.Error);
            Assert.Equal(new[] { "Resource Patient/1 was not indexed." }, jobResult.Warnings);
            Assert.Equal(0, jobResult.OmittedWarningCount);
        }

        [Fact]
        public void GivenTheSameWarningTwice_WhenAdded_ThenItIsKeptOnceAndNotCountedAsOmitted()
        {
            var result = new ReindexProcessingJobResult();

            result.AddWarnings(new[] { "same", "same" });
            result.AddWarnings(new[] { "same" });

            Assert.Equal(new[] { "same" }, result.Warnings);
            Assert.Equal(0, result.OmittedWarningCount);
        }

        [Fact]
        public void GivenMoreWarningsThanTheMaximum_WhenAdded_ThenTheRestAreCounted()
        {
            var result = new ReindexProcessingJobResult();

            result.AddWarnings(Enumerable.Range(0, ReindexProcessingJobResult.MaxWarnings - 1).Select(i => $"w{i}"));
            result.AddWarnings(new[] { "last", "omitted1", "omitted2" });
            result.AddWarnings(null);

            Assert.Equal(ReindexProcessingJobResult.MaxWarnings, result.Warnings.Count);
            Assert.Equal("last", result.Warnings[^1]);
            Assert.Equal(2, result.OmittedWarningCount);

            var roundTripped = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(JsonConvert.SerializeObject(result));
            Assert.Equal(result.Warnings, roundTripped.Warnings);
            Assert.Equal(2, roundTripped.OmittedWarningCount);
        }

        [Fact]
        public void GivenAResultFromAnOlderVersion_WhenDeserialized_ThenItHasNoWarnings()
        {
            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>("{\"SucceededResourceCount\":5,\"Error\":null}");

            Assert.Equal(5, result.SucceededResourceCount);
            Assert.Null(result.Warnings);
            Assert.Equal(0, result.OmittedWarningCount);
        }

        [Fact]
        public async Task ComputeAndWrite_WithValidResults_UpdatesAllResources()
        {
            var resourceType = "Patient";
            var resources = new List<ResourceWrapper>()
            {
                new ResourceWrapper(
                    "1",
                    "1",
                    resourceType,
                    new RawResource("data1", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
                new ResourceWrapper(
                    "2",
                    "1",
                    resourceType,
                    new RawResource("data2", FhirResourceFormat.Json, isMetaSet: false),
                    null,
                    DateTimeOffset.MinValue,
                    false,
                    null,
                    null,
                    null),
            };

            var job = _reindexProcessingJobTaskFactory();

            await job.ComputeAndWrite(resources, _fhirDataStore, _cancellationToken);

            await _resourceWrapperFactory.Received(1).UpdateAsync(resources, _cancellationToken);
            _resourceWrapperFactory.DidNotReceive().Update(Arg.Any<ResourceWrapper>());
            await _fhirDataStore.Received(1).BulkUpdateSearchParameterIndicesAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(r => r.Count == 2),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenTheFirstWriteAttemptIsThrottled_WhenExecuted_ThenIndexingIsNotRepeatedAndTheRetryWarningsAreReported()
        {
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = "Patient",
                ResourceCount = new SearchResultReindex() { Count = 1, StartResourceSurrogateId = 1, EndResourceSurrogateId = 100 },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };
            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);
            var jobInfo = new JobInfo() { Id = 1, Definition = JsonConvert.SerializeObject(job), QueueType = (byte)QueueType.Reindex, GroupId = 1, CreateDate = DateTime.UtcNow, Status = JobStatus.Running };
            SearchResultEntry entry = CreateSearchResultEntry("1", "Patient");
            var computedIndices = new List<SearchIndexEntry> { new SearchIndexEntry(new SearchParameterInfo("ext-uri", "ext-uri"), new StringSearchValue("value")) };
            entry.Resource.UpdateSearchIndices(computedIndices);
            _searchService.SearchBySurrogateIdRange(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(new SearchResult(new[] { entry }, null, null, new List<Tuple<string, string>>()));
            var indicesSeenByTheDataStore = new List<int>();
            int attempts = 0;
            _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    ResourceWrapper resource = callInfo.Arg<IReadOnlyCollection<ResourceWrapper>>().Single();
                    indicesSeenByTheDataStore.Add(resource.SearchIndices.Count);

                    // Like the SQL data store, remove the values it can't index from the wrapper before the write fails.
                    resource.UpdateSearchIndices(new List<SearchIndexEntry>());
                    return ++attempts == 1
                        ? Task.FromException<IReadOnlyList<string>>(new RequestRateExceededException(TimeSpan.FromMilliseconds(1)))
                        : Task.FromResult<IReadOnlyList<string>>(new List<string> { "Resources of type Patient were not indexed." });
                });

            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            await _resourceWrapperFactory.Received(1).UpdateAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
            Assert.Equal(new[] { 1, 1 }, indicesSeenByTheDataStore);
            Assert.Equal(1, jobResult.SucceededResourceCount);
            Assert.Equal(new[] { "Resources of type Patient were not indexed." }, jobResult.Warnings);
        }

        [Fact]
        public async Task ComputeAndWrite_WhenIndexingFails_DoesNotPersistOrRetryIndexing()
        {
            var resources = Array.Empty<ResourceWrapper>();
            var failure = new InvalidOperationException("indexing failed");
            _resourceWrapperFactory.UpdateAsync(resources, _cancellationToken).Returns(Task.FromException(failure));
            var job = _reindexProcessingJobTaskFactory();

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => job.ComputeAndWrite(resources, _fhirDataStore, _cancellationToken));

            Assert.Same(failure, actual);
            await _resourceWrapperFactory.Received(1).UpdateAsync(resources, _cancellationToken);
            await _fhirDataStore.DidNotReceive().BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ComputeAndWrite_WhenIndexingIsPending_WaitsBeforePersisting()
        {
            var resources = Array.Empty<ResourceWrapper>();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _resourceWrapperFactory.UpdateAsync(resources, _cancellationToken).Returns(completion.Task);
            var job = _reindexProcessingJobTaskFactory();

            Task write = job.ComputeAndWrite(resources, _fhirDataStore, _cancellationToken);

            Assert.False(write.IsCompleted);
            await _fhirDataStore.DidNotReceive().BulkUpdateSearchParameterIndicesAsync(Arg.Any<IReadOnlyCollection<ResourceWrapper>>(), Arg.Any<CancellationToken>());
            completion.SetResult();
            await write;
            await _resourceWrapperFactory.Received(1).UpdateAsync(resources, _cancellationToken);
            await _fhirDataStore.Received(1).BulkUpdateSearchParameterIndicesAsync(resources, _cancellationToken);
        }

        [Fact]
        public async Task ComputeAndWrite_WhenPersistenceIsRetried_DoesNotRepeatIndexing()
        {
            var resources = Array.Empty<ResourceWrapper>();
            _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(resources, _cancellationToken)
                .Returns(Task.FromException<IReadOnlyList<string>>(new RequestRateExceededException(TimeSpan.Zero)), Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
            var job = _reindexProcessingJobTaskFactory();

            await job.ComputeAndWrite(resources, _fhirDataStore, _cancellationToken);

            await _resourceWrapperFactory.Received(1).UpdateAsync(resources, _cancellationToken);
            await _fhirDataStore.Received(2).BulkUpdateSearchParameterIndicesAsync(resources, _cancellationToken);
        }

        [Fact]
        public async Task GivenCmkFailure_WhenProcessingExecutes_ThenSoftFailureIsCustomerCaused()
        {
            // Arrange
            var definition = new ReindexProcessingJobDefinition
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = "Patient",
                ResourceCount = new SearchResultReindex { Count = 1, StartResourceSurrogateId = 1, EndResourceSurrogateId = 100 },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };
            _searchParameterOperations.GetSearchParameterHash("Patient").Returns("patientHash");
            var failure = new CustomerManagedKeyException("Customer managed key is inaccessible.");
            _searchService.SearchBySurrogateIdRange(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<SearchResult>(failure));
            var jobInfo = new JobInfo { Id = 1, Definition = JsonConvert.SerializeObject(definition) };

            // Act
            var exception = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(() => _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            // Assert
            Assert.True(exception.IsCustomerCaused);
            Assert.Same(failure, exception.InnerException);
            var result = Assert.IsType<ReindexProcessingJobResult>(exception.Error);
            Assert.Equal(failure.Message, result.Error);
            var serializedResult = JObject.Parse(JsonConvert.SerializeObject(result));
            Assert.True(serializedResult["isCustomerCaused"].Value<bool>());
        }

        [Fact]
        public async Task ProcessQueryAsync_WithSearchServiceException_ThrowsReindexException()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Throw exception from search service (SQL path uses SearchBySurrogateIdRange)
            _searchService.SearchBySurrogateIdRange(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException<SearchResult>(new InvalidOperationException("Search service error")));

            // When search service throws an exception, the job should throw JobExecutionSoftFailureException with error in result
            var exception = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(
                async () => await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            var jobResult = exception.Error as ReindexProcessingJobResult;
            string errorMessage = Assert.IsType<string>(jobResult?.Error);
            Assert.Contains("Search service error", errorMessage);
            Assert.False(exception.IsCustomerCaused);
        }

        [Fact]
        public async Task ProcessQueryAsync_WithGeneralException_CatchesAndSetsError()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 100,
                    StartResourceSurrogateId = 1,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 1)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchBySurrogateIdRange(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            // Throw general exception from bulk update
            _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(
                Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("General error during bulk update")));

            // Expect JobExecutionSoftFailureException to be thrown
            var exception = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(
                async () => await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            var jobResult = exception.Error as ReindexProcessingJobResult;
            string errorMessage = Assert.IsType<string>(jobResult?.Error);
            Assert.Contains("General error", errorMessage);
        }

        [Fact]
        public async Task GetResourcesToReindexAsync_WithContinuationToken_IncludesTokenInQuery()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 5,
                    EndResourceSurrogateId = 0,
                    StartResourceSurrogateId = 0,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 5)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.Equal(5, jobResult.SucceededResourceCount);
        }

        [Fact]
        public async Task ExecuteAsync_WithCosmosContinuationToken_ProcessesAllWriteBatches()
        {
            var expectedResourceType = "Patient";
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 5,
                MaximumNumberOfResourcesPerWrite = 2,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 5,
                    StartResourceSurrogateId = 0,
                    EndResourceSurrogateId = 0,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var requestedCounts = new List<int>();
            var requestedContinuationTokens = new List<string>();
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(callInfo =>
                {
                    var parameters = callInfo.ArgAt<IReadOnlyList<Tuple<string, string>>>(0);
                    requestedCounts.Add(int.Parse(parameters.Single(parameter => parameter.Item1 == KnownQueryParameterNames.Count).Item2));
                    requestedContinuationTokens.Add(parameters.SingleOrDefault(parameter => parameter.Item1 == KnownQueryParameterNames.ContinuationToken)?.Item2);

                    return requestedCounts.Count switch
                    {
                        1 => new SearchResult(
                            Enumerable.Range(1, 2).Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType)).ToList(),
                            "continuation-1",
                            null,
                            new List<Tuple<string, string>>()),
                        2 => new SearchResult(
                            Enumerable.Range(3, 2).Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType)).ToList(),
                            "continuation-2",
                            null,
                            new List<Tuple<string, string>>()),
                        3 => new SearchResult(
                            new List<SearchResultEntry> { CreateSearchResultEntry("5", expectedResourceType), CreateSearchResultEntry("6", expectedResourceType) },
                            null,
                            null,
                            new List<Tuple<string, string>>()),
                        _ => throw new InvalidOperationException("Unexpected query count."),
                    };
                });

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.Equal(6, jobResult.SucceededResourceCount);
            Assert.Equal(new[] { 2, 2, 2 }, requestedCounts);

            var expectedContinuationTokens = new[]
            {
                null,
                Convert.ToBase64String(Encoding.UTF8.GetBytes("continuation-1")),
                Convert.ToBase64String(Encoding.UTF8.GetBytes("continuation-2")),
            };
            Assert.Equal(expectedContinuationTokens, requestedContinuationTokens);
            await _fhirDataStore.Received(3).BulkUpdateSearchParameterIndicesAsync(
                Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                Arg.Any<CancellationToken>());
            await _fhirDataStore.Received(3).BulkUpdateSearchParameterIndicesAsync(
                Arg.Is<IReadOnlyCollection<ResourceWrapper>>(resources => resources.Count == 2),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task ExecuteAsync_WithLargeSurrogateIdRange_ProcessesSingleBatch()
        {
            var expectedResourceType = "Patient";
            var startId = 100L;
            var endId = 5000L;
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 10000,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 6,
                    EndResourceSurrogateId = endId,
                    StartResourceSurrogateId = startId,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, 6)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            var callCount = 0;
            var surrogatIdCallCount = 0;

            // Mock SearchBySurrogateIdRange (SQL path)
            _searchService.SearchBySurrogateIdRange(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    surrogatIdCallCount++;
                    return new SearchResult(
                        searchResultEntries,
                        null,
                        null,
                        new List<Tuple<string, string>>())
                    {
                        MaxResourceSurrogateId = endId,
                        TotalCount = 6,
                    };
                });

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(_ =>
                {
                    callCount++;
                    return new SearchResult(
                        searchResultEntries,
                        null,
                        null,
                        new List<Tuple<string, string>>())
                    {
                        MaxResourceSurrogateId = endId,
                        TotalCount = 6,
                    };
                });

            var result = await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken);
            var jobResult = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(result);

            Assert.Equal(6, jobResult.SucceededResourceCount);
            Assert.Equal(1, surrogatIdCallCount);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task WhenOutOfMemoryException_ThenRetriesWorkCorrectly(bool throwOnRead)
        {
            ReindexProcessingJob.OomRetryDelayBaseSec = 1;

            var expectedResourceType = "Patient";
            var initialBatchSize = 1000;
            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = initialBatchSize,
                MaximumNumberOfResourcesPerWrite = initialBatchSize,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = 1,
                    EndResourceSurrogateId = 0,
                    StartResourceSurrogateId = 0,
                },
                SearchParameterHash = "patientHash",
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(job.SearchParameterHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var requestedCounts = new List<int>();
            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(callInfo =>
                {
                    var parameters = callInfo.ArgAt<IReadOnlyList<Tuple<string, string>>>(0);
                    requestedCounts.Add(int.Parse(parameters.Single(parameter => parameter.Item1 == KnownQueryParameterNames.Count).Item2));

                    if (throwOnRead)
                    {
                        return Task.FromException<SearchResult>(new OutOfMemoryException("Simulated OOM"));
                    }

                    return Task.FromResult(new SearchResult(
                        new List<SearchResultEntry> { CreateSearchResultEntry("1", expectedResourceType) },
                        null,
                        null,
                        new List<Tuple<string, string>>()));
                });

            if (!throwOnRead)
            {
                _fhirDataStore.BulkUpdateSearchParameterIndicesAsync(
                    Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                    Arg.Any<CancellationToken>())
                    .Returns(_ => Task.FromException<IReadOnlyList<string>>(new OutOfMemoryException("Simulated OOM")));
            }

            var exception = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(
                async () => await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            var jobResult = Assert.IsType<ReindexProcessingJobResult>(exception.Error);
            var errorMessage = Assert.IsType<string>(jobResult.Error);

            Assert.Equal(new[] { 1000, 100, 10, 1 }, requestedCounts);
            Assert.Equal("Simulated OOM", errorMessage);
            Assert.IsType<OutOfMemoryException>(exception.InnerException);

            if (throwOnRead)
            {
                await _fhirDataStore.DidNotReceive().BulkUpdateSearchParameterIndicesAsync(
                    Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                    Arg.Any<CancellationToken>());
            }
            else
            {
                await _fhirDataStore.Received(4).BulkUpdateSearchParameterIndicesAsync(
                    Arg.Any<IReadOnlyCollection<ResourceWrapper>>(),
                    Arg.Any<CancellationToken>());
            }
        }

        [Fact]
        public async Task CheckDiscrepancies_WhenHashMismatch_ThrowsReindexJobException()
        {
            // Arrange
            var expectedResourceType = "Account";
            var requestedHash = "orchestratorHash";
            var staleHash = "staleHash";

            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = _mockedSearchCount,
                    EndResourceSurrogateId = 0,
                    StartResourceSurrogateId = 0,
                },
                SearchParameterHash = requestedHash,
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(staleHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Act & Assert - Job should fail immediately on mismatch
            var exception = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(
                async () => await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            Assert.Contains($"ResourceType={expectedResourceType} SearchParameterHash: Requested={requestedHash} != Local={staleHash}", exception.Message);
        }

        [Fact]
        public async Task CheckDiscrepancies_WhenHashMismatch_DoesNotWaitForRefresh()
        {
            // Arrange
            var expectedResourceType = "Account";
            var requestedHash = "orchestratorHash";
            var staleHash = "staleHash";

            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = _mockedSearchCount,
                    EndResourceSurrogateId = 0,
                    StartResourceSurrogateId = 0,
                },
                SearchParameterHash = requestedHash,
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>())
                .Returns(staleHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            // Act & Assert - Job should fail without trying to self-heal
            var exception = await Assert.ThrowsAsync<JobExecutionSoftFailureException>(
                async () => await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            Assert.Contains($"ResourceType={expectedResourceType} SearchParameterHash: Requested={requestedHash} != Local={staleHash}", exception.Message);
        }

        [Fact]
        public async Task CheckDiscrepancies_WhenHashMatches_DoesNotWaitForRefresh()
        {
            // Arrange
            var expectedResourceType = "Account";
            var matchingHash = "matchingHash";

            var job = new ReindexProcessingJobDefinition()
            {
                MaximumNumberOfResourcesPerQuery = 100,
                MaximumNumberOfResourcesPerWrite = 100,
                ResourceType = expectedResourceType,
                ResourceCount = new SearchResultReindex()
                {
                    Count = _mockedSearchCount,
                    EndResourceSurrogateId = 0,
                    StartResourceSurrogateId = 0,
                },
                SearchParameterHash = matchingHash,
                TypeId = (int)JobType.ReindexProcessing,
            };

            _searchParameterOperations.GetSearchParameterHash(Arg.Any<string>()).Returns(matchingHash);

            var jobInfo = new JobInfo()
            {
                Id = 1,
                Definition = JsonConvert.SerializeObject(job),
                QueueType = (byte)QueueType.Reindex,
                GroupId = 1,
                CreateDate = DateTime.UtcNow,
                Status = JobStatus.Running,
            };

            var searchResultEntries = Enumerable.Range(1, _mockedSearchCount)
                .Select(i => CreateSearchResultEntry(i.ToString(), expectedResourceType))
                .ToList();

            _searchService.SearchForReindexAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<string>(),
                false,
                Arg.Any<CancellationToken>(),
                true)
                .Returns(new SearchResult(
                    searchResultEntries,
                    null,
                    null,
                    new List<Tuple<string, string>>()));

            // Act
            var result = JsonConvert.DeserializeObject<ReindexProcessingJobResult>(
                await _reindexProcessingJobTaskFactory().ExecuteAsync(jobInfo, _cancellationToken));

            // Assert - Job succeeded
            Assert.Equal(_mockedSearchCount, result.SucceededResourceCount);
        }
    }
}
