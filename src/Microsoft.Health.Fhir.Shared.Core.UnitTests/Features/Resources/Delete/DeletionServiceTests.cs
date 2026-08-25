// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Delete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class DeletionServiceTests
    {
        private readonly IResourceWrapperFactory _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        private readonly Lazy<IConformanceProvider> _conformanceProvider = new Lazy<IConformanceProvider>(() => Substitute.For<IConformanceProvider>());
        private readonly IDeletionServiceDataStoreFactory _dataStoreFactory = Substitute.For<IDeletionServiceDataStoreFactory>();
        private readonly IScopeProvider<ISearchService> _searchServiceFactory = Substitute.For<IScopeProvider<ISearchService>>();
        private readonly ResourceIdProvider _resourceIdProvider = Substitute.For<ResourceIdProvider>();
        private readonly FhirRequestContextAccessor _contextAccessor = Substitute.For<FhirRequestContextAccessor>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly IFhirRuntimeConfiguration _fhirRuntimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly IResourceDeserializer _resourceDeserializer = Substitute.For<IResourceDeserializer>();
        private readonly ILogger<DeletionService> _logger = Substitute.For<ILogger<DeletionService>>();
        private readonly IModelInfoProvider _modelInfoProvider = Substitute.For<IModelInfoProvider>();
        private readonly DeletionService _service;

        public DeletionServiceTests()
        {
            var config = new CoreFeatureConfiguration();
            var configuration = Options.Create(config);

            var dummyRequestContext = new FhirRequestContext(
                "DELETE",
                "https://localhost/Patient",
                "https://localhost/",
                Guid.NewGuid().ToString(),
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>());
            _contextAccessor.RequestContext.Returns(dummyRequestContext);
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);

            _service = new DeletionService(
                _resourceWrapperFactory,
                _conformanceProvider,
                _dataStoreFactory,
                _searchServiceFactory,
                _resourceIdProvider,
                _contextAccessor,
                _auditLogger,
                configuration,
                _fhirRuntimeConfiguration,
                _searchParameterOperations,
                _resourceDeserializer,
                _logger,
                _modelInfoProvider);
        }

        [Fact]
        public async Task GivenBulkHardDelete_WhenResourcesAreDeleted_ThenAuditLoggerIsCalledWithBatchedAffectedItems()
        {
            // Arrange
            var resourceType = "Patient";
            var parameters = new List<Tuple<string, string>>()
            {
                Tuple.Create("_lastUpdated", "2000-01-01T00:00:00Z"),
            };

            var request = new ConditionalDeleteResourceRequest(
                resourceType,
                parameters,
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: false);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            var entries = new List<SearchResultEntry>();
            for (int i = 0; i < 3; i++)
            {
                var resource = Samples.GetDefaultPatient().ToPoco<Patient>();
                resource.Id = $"id-{i}";
                resource.VersionId = "1";

                var resourceElement = resource.ToResourceElement();
                var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
                var resourceRequest = Substitute.For<ResourceRequest>();
                var compartmentIndices = Substitute.For<CompartmentIndices>();
                var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, null, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
                entries.Add(new SearchResultEntry(wrapper, SearchEntryMode.Match));
            }

            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()).Returns(
                Task.FromResult(new SearchResult(entries, null, null, Array.Empty<Tuple<string, string>>())));

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedDataStore = new DeletionServiceScopedDataStore(fhirDataStore);
            _dataStoreFactory.GetScopedDataStore().Returns(scopedDataStore);

            // Act
            await _service.DeleteMultipleAsync(request, CancellationToken.None);

            // Wait for Task.Run-based audit logging to complete (poll for the expected call)
            await BulkOperationAuditLogHelperTests.WaitForAuditLogCall(_auditLogger);

            // Assert - verify audit logger was called with "Affected Items" property (produced by BulkOperationAuditLogHelper)
            _auditLogger.Received().LogAudit(
                Arg.Any<AuditAction>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Uri>(),
                Arg.Any<HttpStatusCode?>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<IReadOnlyDictionary<string, string>>(d => d.ContainsKey("Affected Items")));
        }

        [Fact]
        public async Task GivenSearchParameterDelete_WhenConcurrencyConflictOccurs_ThenRetries()
        {
            var resourceType = "SearchParameter";
            var parameters = new List<Tuple<string, string>>()
            {
                Tuple.Create("url", "http://test.com/param"),
            };

            var request = new ConditionalDeleteResourceRequest(
                resourceType,
                parameters,
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: false);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            var searchParameter = new SearchParameter { Id = "test", Url = "http://test.com/param" };
            var resource = searchParameter.ToResourceElement();
            var rawResource = new RawResource(searchParameter.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = Substitute.For<ResourceRequest>();
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var wrapper = new ResourceWrapper(resource, rawResource, resourceRequest, false, null, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
            var entries = new List<SearchResultEntry> { new SearchResultEntry(wrapper, SearchEntryMode.Match) };

            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()).Returns(
                Task.FromResult(new SearchResult(entries, null, null, Array.Empty<Tuple<string, string>>())));

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedDataStore = new DeletionServiceScopedDataStore(fhirDataStore);
            _dataStoreFactory.GetScopedDataStore().Returns(scopedDataStore);

            var attemptCount = 0;
            _searchParameterOperations
                .DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(callInfo =>
                {
                    attemptCount++;
                    if (attemptCount < 3)
                    {
                        throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict);
                    }

                    return Task.CompletedTask;
                });

            await _service.DeleteMultipleAsync(request, CancellationToken.None);

            Assert.Equal(3, attemptCount);
        }

        [Fact]
        public async Task GivenSearchParameterDelete_WhenConcurrencyConflictExhaustsRetries_ThenThrowsWithRetryCount()
        {
            var resourceType = "SearchParameter";
            var parameters = new List<Tuple<string, string>>()
            {
                Tuple.Create("url", "http://test.com/param"),
            };

            var request = new ConditionalDeleteResourceRequest(
                resourceType,
                parameters,
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: false);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            var searchParameter = new SearchParameter { Id = "test", Url = "http://test.com/param" };
            var resource = searchParameter.ToResourceElement();
            var rawResource = new RawResource(searchParameter.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = Substitute.For<ResourceRequest>();
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var wrapper = new ResourceWrapper(resource, rawResource, resourceRequest, false, null, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
            var entries = new List<SearchResultEntry> { new SearchResultEntry(wrapper, SearchEntryMode.Match) };

            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()).Returns(
                Task.FromResult(new SearchResult(entries, null, null, Array.Empty<Tuple<string, string>>())));

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedDataStore = new DeletionServiceScopedDataStore(fhirDataStore);
            _dataStoreFactory.GetScopedDataStore().Returns(scopedDataStore);

            _searchParameterOperations
                .DeleteSearchParameterAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(_ => throw new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict));

            var exception = await Assert.ThrowsAsync<IncompleteOperationException<IDictionary<string, long>>>(async () =>
                await _service.DeleteMultipleAsync(request, CancellationToken.None));

            Assert.Contains(" Deletion.3", exception.InnerException.Message);
        }

        [Fact]
        public async Task GivenBulkHardDelete_WhenSearchServiceThrowsConnectionExceptionOnSecondPage_ThenReturnsIncompleteOperationException()
        {
            // Arrange
            var resourceType = "Patient";
            var parameters = new List<Tuple<string, string>>()
            {
                Tuple.Create("_lastUpdated", "2000-01-01T00:00:00Z"),
            };

            var request = new ConditionalDeleteResourceRequest(
                resourceType,
                parameters,
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: false);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // First page of results - returns 5 entries with a continuation token
            var firstPageEntries = new List<SearchResultEntry>();
            for (int i = 0; i < 5; i++)
            {
                var resource = Samples.GetDefaultPatient().ToPoco<Patient>();
                resource.Id = $"id-{i}";
                resource.VersionId = "1";

                var resourceElement = resource.ToResourceElement();
                var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
                var resourceRequest = Substitute.For<ResourceRequest>();
                var compartmentIndices = Substitute.For<CompartmentIndices>();
                var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, null, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
                firstPageEntries.Add(new SearchResultEntry(wrapper, SearchEntryMode.Match));
            }

            var callCount = 0;
            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()).Returns(async callInfo =>
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        // First call returns results with continuation token
                        return new SearchResult(firstPageEntries, "continuation-token-1", null, Array.Empty<Tuple<string, string>>());
                    }
                    else
                    {
                        // Second call throws a connection exception (simulating network failure)
                        await Task.Delay(100); // Simulate some delay so that the first call can complete
                        throw new InvalidOperationException("A transport-level error has occurred when receiving results from the server.");
                    }
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            fhirDataStore.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            var scopedDataStore = new DeletionServiceScopedDataStore(fhirDataStore);
            _dataStoreFactory.GetScopedDataStore().Returns(scopedDataStore);

            // Act
            var exception = await Assert.ThrowsAsync<IncompleteOperationException<IDictionary<string, long>>>(async () =>
                await _service.DeleteMultipleAsync(request, CancellationToken.None));

            // Assert
            Assert.NotNull(exception);
            Assert.NotNull(exception.InnerException);
            Assert.IsType<AggregateException>(exception.InnerException);

            var aggregateException = (AggregateException)exception.InnerException;
            Assert.Contains(aggregateException.InnerExceptions, ex => ex is InvalidOperationException);

            // Verify that partial results contain the first page of deleted resources
            Assert.NotNull(exception.PartialResults);
            Assert.True(exception.PartialResults.TryGetValue("Patient", out long deletedPatientCount));
            Assert.Equal(5, deletedPatientCount);

            // Verify search service was called twice (first page succeeded, second page failed)
            await searchService.Received(2).SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>());
        }

        [Fact]
        public async Task GivenSoftDeleteWithETag_WhenETagIsRequired_ThenUpsertUsesClientETagAndPolicy()
        {
            // Arrange
            var fhirDataStore = SetUpDataStore();
            var deletedWrapper = CreateWrapper(version: null);
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), deleted: true, keepMeta: false)
                .Returns(deletedWrapper);
            fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new UpsertOutcome(deletedWrapper, SaveOutcomeType.Updated)));
            _conformanceProvider.Value.SatisfiesAsync(Arg.Any<IReadOnlyCollection<CapabilityQuery>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            var request = new DeleteResourceRequest(
                "Patient",
                "id",
                DeleteOperation.SoftDelete,
                weakETag: WeakETag.FromVersionId("7"));

            // Act
            await _service.DeleteAsync(request, CancellationToken.None);

            // Assert
            await fhirDataStore.Received().UpsertAsync(
                Arg.Is<ResourceWrapperOperation>(operation =>
                    operation.WeakETag.VersionId == "7" &&
                    operation.RequireETagOnUpdate),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenStu3SoftDeleteWithStaleETag_WhenDataStoreReturnsConflict_ThenThrowsPreconditionFailed()
        {
            // Arrange
            var fhirDataStore = SetUpDataStore();
            var deletedWrapper = CreateWrapper(version: null);
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), deleted: true, keepMeta: false)
                .Returns(deletedWrapper);
            fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<UpsertOutcome>(new ResourceConflictException(WeakETag.FromVersionId("6"))));
            _modelInfoProvider.Version.Returns(FhirSpecification.Stu3);
            var request = new DeleteResourceRequest(
                "Patient",
                "id",
                DeleteOperation.SoftDelete,
                weakETag: WeakETag.FromVersionId("6"));

            // Act and assert
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _service.DeleteAsync(request, CancellationToken.None));
        }

        [Fact]
        public async Task GivenStu3SoftDeleteWithETag_WhenDataStoreReturnsConcurrentWriteConflict_ThenPreservesConflict()
        {
            // Arrange
            var fhirDataStore = SetUpDataStore();
            var deletedWrapper = CreateWrapper(version: null);
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), deleted: true, keepMeta: false)
                .Returns(deletedWrapper);
            fhirDataStore.UpsertAsync(Arg.Any<ResourceWrapperOperation>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<UpsertOutcome>(new ResourceConflictException("Concurrent write conflict.")));
            _modelInfoProvider.Version.Returns(FhirSpecification.Stu3);
            var request = new DeleteResourceRequest(
                "Patient",
                "id",
                DeleteOperation.SoftDelete,
                weakETag: WeakETag.FromVersionId("6"));

            // Act and assert
            await Assert.ThrowsAsync<ResourceConflictException>(() => _service.DeleteAsync(request, CancellationToken.None));
        }

        [Theory]
        [InlineData(DeleteOperation.HardDelete)]
        [InlineData(DeleteOperation.PurgeHistory)]
        public async Task GivenDestructiveDeleteWithMatchingETag_WhenETagIsRequired_ThenDeletes(DeleteOperation deleteOperation)
        {
            // Arrange
            var fhirDataStore = SetUpDataStore();
            fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateWrapper("7")));
            _conformanceProvider.Value.SatisfiesAsync(Arg.Any<IReadOnlyCollection<CapabilityQuery>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            var request = new DeleteResourceRequest(
                "Patient",
                "id",
                deleteOperation,
                weakETag: WeakETag.FromVersionId("7"));

            // Act
            await _service.DeleteAsync(request, CancellationToken.None);

            // Assert
            await fhirDataStore.Received().GetAsync(request.ResourceKey, CancellationToken.None);
            await fhirDataStore.Received().HardDeleteAsync(
                request.ResourceKey,
                deleteOperation == DeleteOperation.PurgeHistory,
                false,
                CancellationToken.None);
        }

        [Theory]
        [InlineData(DeleteOperation.HardDelete)]
        [InlineData(DeleteOperation.PurgeHistory)]
        public async Task GivenDestructiveDeleteWithStaleETag_WhenCurrentResourceExists_ThenRejectsWithoutDeleting(DeleteOperation deleteOperation)
        {
            // Arrange
            var fhirDataStore = SetUpDataStore();
            fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateWrapper("7")));
            var request = new DeleteResourceRequest(
                "Patient",
                "id",
                deleteOperation,
                weakETag: WeakETag.FromVersionId("6"));

            // Act
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _service.DeleteAsync(request, CancellationToken.None));

            // Assert
            await fhirDataStore.DidNotReceive().HardDeleteAsync(
                Arg.Any<ResourceKey>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(DeleteOperation.HardDelete, FhirSpecification.R4)]
        [InlineData(DeleteOperation.PurgeHistory, FhirSpecification.R4)]
        [InlineData(DeleteOperation.HardDelete, FhirSpecification.Stu3)]
        [InlineData(DeleteOperation.PurgeHistory, FhirSpecification.Stu3)]
        public async Task GivenDestructiveDeleteWithoutETag_WhenETagIsRequired_ThenRejectsWithoutDeleting(DeleteOperation deleteOperation, FhirSpecification fhirSpecification)
        {
            // Arrange
            var fhirDataStore = SetUpDataStore();
            fhirDataStore.GetAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateWrapper("7")));
            _conformanceProvider.Value.SatisfiesAsync(Arg.Any<IReadOnlyCollection<CapabilityQuery>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));
            _modelInfoProvider.Version.Returns(fhirSpecification);
            var request = new DeleteResourceRequest("Patient", "id", deleteOperation);

            // Act
            if (fhirSpecification == FhirSpecification.Stu3)
            {
                await Assert.ThrowsAsync<PreconditionFailedException>(() => _service.DeleteAsync(request, CancellationToken.None));
            }
            else
            {
                await Assert.ThrowsAsync<BadRequestException>(() => _service.DeleteAsync(request, CancellationToken.None));
            }

            // Assert
            await fhirDataStore.DidNotReceive().HardDeleteAsync(
                Arg.Any<ResourceKey>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenSingleMatchWithIncludeAndStaleWeakETag_WhenSoftDeletingConditionally_ThenNoResourcesAreDeleted()
        {
            // Arrange
            const string matchId = "match";
            const string includeId = "include";
            WeakETag weakETag = WeakETag.FromVersionId("7");
            var request = CreateSingleMatchConditionalDeleteRequest(DeleteOperation.SoftDelete, weakETag);
            SetUpConditionalSearch(
                CreateSearchResultEntry(KnownResourceTypes.Group, matchId, weakETag.VersionId, SearchEntryMode.Match),
                CreateSearchResultEntry(KnownResourceTypes.Patient, includeId, "1", SearchEntryMode.Include));
            SetUpDeletedWrapperFactory();
            var fhirDataStore = SetUpDataStore();
            var deletedResourceIds = new List<string>();
            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    IReadOnlyList<ResourceWrapperOperation> operations = callInfo.ArgAt<IReadOnlyList<ResourceWrapperOperation>>(0);
                    var outcomes = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();
                    foreach (ResourceWrapperOperation operation in operations)
                    {
                        if (operation.Wrapper.ResourceId == matchId && operation.WeakETag?.VersionId == weakETag.VersionId)
                        {
                            outcomes.Add(
                                operation.GetIdentifier(),
                                new DataStoreOperationOutcome(new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId))));
                        }
                        else
                        {
                            deletedResourceIds.Add(operation.Wrapper.ResourceId);
                            outcomes.Add(operation.GetIdentifier(), new DataStoreOperationOutcome(new UpsertOutcome(operation.Wrapper, SaveOutcomeType.Updated)));
                        }
                    }

                    return Task.FromResult(new MergeOutcome(MergeOutcomeFinalState.Completed, outcomes));
                });

            // Act and assert
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _service.DeleteMultipleAsync(request, CancellationToken.None));

            Assert.DoesNotContain(includeId, deletedResourceIds);
            Assert.DoesNotContain(matchId, deletedResourceIds);
        }

        [Fact]
        public async Task GivenSearchParameterMatchWithIncludeAndStaleWeakETag_WhenSoftDeletingConditionally_ThenNoResourcesAreDeleted()
        {
            // Arrange
            const string matchId = "search-parameter";
            const string includeId = "include";
            WeakETag weakETag = WeakETag.FromVersionId("7");
            var request = CreateSingleMatchConditionalDeleteRequest(DeleteOperation.SoftDelete, weakETag, KnownResourceTypes.SearchParameter);
            SetUpConditionalSearch(
                CreateSearchResultEntry(KnownResourceTypes.SearchParameter, matchId, weakETag.VersionId, SearchEntryMode.Match),
                CreateSearchResultEntry(KnownResourceTypes.Patient, includeId, "1", SearchEntryMode.Include));
            SetUpDeletedWrapperFactory();
            var fhirDataStore = SetUpDataStore();
            fhirDataStore.GetAsync(Arg.Is<ResourceKey>(key => key.Id == matchId), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateWrapper(KnownResourceTypes.SearchParameter, matchId, "8")));
            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(MergeOutcome.Empty));

            // Act and assert
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _service.DeleteMultipleAsync(request, CancellationToken.None));

            await fhirDataStore.DidNotReceive().MergeAsync(
                Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(),
                Arg.Any<CancellationToken>());
            await _searchParameterOperations.DidNotReceive().DeleteSearchParameterAsync(
                Arg.Any<RawResource>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<bool>());
        }

        [Fact]
        public async Task GivenSingleMatchWithIncludeAndMatchingWeakETag_WhenHardDeletingConditionally_ThenOnlyTheMatchIsVersionChecked()
        {
            // Arrange
            const string matchId = "match";
            const string includeId = "include";
            WeakETag weakETag = WeakETag.FromVersionId("7");
            var request = CreateSingleMatchConditionalDeleteRequest(DeleteOperation.HardDelete, weakETag);
            SetUpConditionalSearch(
                CreateSearchResultEntry(KnownResourceTypes.Group, matchId, weakETag.VersionId, SearchEntryMode.Match),
                CreateSearchResultEntry(KnownResourceTypes.Patient, includeId, "1", SearchEntryMode.Include));
            var fhirDataStore = SetUpDataStore();
            fhirDataStore.GetAsync(Arg.Is<ResourceKey>(key => key.Id == matchId), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateWrapper(KnownResourceTypes.Group, matchId, weakETag.VersionId)));
            fhirDataStore.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await _service.DeleteMultipleAsync(request, CancellationToken.None);

            // Assert
            await fhirDataStore.Received(1).GetAsync(
                Arg.Is<ResourceKey>(key => key.Id == matchId),
                Arg.Any<CancellationToken>());
            await fhirDataStore.DidNotReceive().GetAsync(
                Arg.Is<ResourceKey>(key => key.Id == includeId),
                Arg.Any<CancellationToken>());
            await fhirDataStore.Received(1).HardDeleteAsync(
                Arg.Is<ResourceKey>(key => key.Id == matchId),
                false,
                false,
                Arg.Any<CancellationToken>());
            await fhirDataStore.Received(1).HardDeleteAsync(
                Arg.Is<ResourceKey>(key => key.Id == includeId),
                false,
                false,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenSingleMatchWithIncludeAndStaleWeakETag_WhenHardDeletingConditionally_ThenNoResourcesAreDeleted()
        {
            // Arrange
            const string matchId = "match";
            const string includeId = "include";
            WeakETag weakETag = WeakETag.FromVersionId("7");
            var request = CreateSingleMatchConditionalDeleteRequest(DeleteOperation.HardDelete, weakETag);
            SetUpConditionalSearch(
                CreateSearchResultEntry(KnownResourceTypes.Group, matchId, weakETag.VersionId, SearchEntryMode.Match),
                CreateSearchResultEntry(KnownResourceTypes.Patient, includeId, "1", SearchEntryMode.Include));
            var fhirDataStore = SetUpDataStore();
            fhirDataStore.GetAsync(Arg.Is<ResourceKey>(key => key.Id == matchId), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(CreateWrapper(KnownResourceTypes.Group, matchId, "8")));
            fhirDataStore.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act and assert
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _service.DeleteMultipleAsync(request, CancellationToken.None));

            await fhirDataStore.DidNotReceive().HardDeleteAsync(
                Arg.Is<ResourceKey>(key => key.Id == matchId),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
            await fhirDataStore.DidNotReceive().HardDeleteAsync(
                Arg.Is<ResourceKey>(key => key.Id == includeId),
                false,
                false,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenMultipleMatchesWithWeakETag_WhenSoftDeletingConditionally_ThenNoMatchOperationCarriesTheETag()
        {
            // Arrange
            WeakETag weakETag = WeakETag.FromVersionId("7");
            var request = new ConditionalDeleteResourceRequest(
                KnownResourceTypes.Group,
                new List<Tuple<string, string>> { Tuple.Create("_tag", "tag") },
                DeleteOperation.SoftDelete,
                maxDeleteCount: 100,
                weakETag: weakETag);
            SetUpConditionalSearch(
                CreateSearchResultEntry(KnownResourceTypes.Group, "first", "7", SearchEntryMode.Match),
                CreateSearchResultEntry(KnownResourceTypes.Group, "second", "7", SearchEntryMode.Match));
            SetUpDeletedWrapperFactory();
            var fhirDataStore = SetUpDataStore();
            var mergedOperations = new List<ResourceWrapperOperation>();
            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    mergedOperations.AddRange(callInfo.ArgAt<IReadOnlyList<ResourceWrapperOperation>>(0));
                    return Task.FromResult(MergeOutcome.Empty);
                });

            // Act
            await _service.DeleteMultipleAsync(request, CancellationToken.None);

            // Assert
            Assert.All(mergedOperations, operation =>
            {
                Assert.Null(operation.WeakETag);
                Assert.False(operation.RequireETagOnUpdate);
            });
        }

        private IFhirDataStore SetUpDataStore()
        {
            var fhirDataStore = Substitute.For<IFhirDataStore>();
            _dataStoreFactory.GetScopedDataStore().Returns(new DeletionServiceScopedDataStore(fhirDataStore));
            return fhirDataStore;
        }

        private ConditionalDeleteResourceRequest CreateSingleMatchConditionalDeleteRequest(DeleteOperation deleteOperation, WeakETag weakETag, string resourceType = KnownResourceTypes.Group)
        {
            var request = new ConditionalDeleteResourceRequest(
                resourceType,
                new List<Tuple<string, string>> { Tuple.Create("_tag", "tag") },
                deleteOperation,
                maxDeleteCount: 1,
                weakETag: weakETag)
            {
                IsSingleResourceConditionalDelete = true,
            };

            _conformanceProvider.Value.SatisfiesAsync(Arg.Any<IReadOnlyCollection<CapabilityQuery>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));

            return request;
        }

        private void SetUpConditionalSearch(params SearchResultEntry[] entries)
        {
            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);
            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>())
                .Returns(Task.FromResult(new SearchResult(entries, null, null, Array.Empty<Tuple<string, string>>())));
        }

        private void SetUpDeletedWrapperFactory()
        {
            _resourceWrapperFactory.Create(Arg.Any<ResourceElement>(), deleted: true, keepMeta: false)
                .Returns(callInfo =>
                {
                    Resource resource = callInfo.ArgAt<ResourceElement>(0).ToPoco<Resource>();
                    return CreateWrapper(resource.TypeName, resource.Id, version: null, deleted: true);
                });
        }

        private static SearchResultEntry CreateSearchResultEntry(string resourceType, string resourceId, string version, SearchEntryMode searchEntryMode)
        {
            return new SearchResultEntry(CreateWrapper(resourceType, resourceId, version), searchEntryMode);
        }

        private static ResourceWrapper CreateWrapper(string version)
        {
            return CreateWrapper("Patient", "id", version);
        }

        private static ResourceWrapper CreateWrapper(string resourceType, string resourceId, string version, bool deleted = false)
        {
            return new ResourceWrapper(
                resourceId,
                version,
                resourceType,
                rawResource: null,
                request: null,
                lastModified: DateTimeOffset.UtcNow,
                deleted,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);
        }
    }
}
