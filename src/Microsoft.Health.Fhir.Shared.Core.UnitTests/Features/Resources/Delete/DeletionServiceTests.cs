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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Security.Authorization;
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
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
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
        private readonly ISupportedProfilesStore _supportedProfiles = Substitute.For<ISupportedProfilesStore>();
        private readonly IAuthorizationService<DataActions> _authorizationService = Substitute.For<IAuthorizationService<DataActions>>();
        private readonly ILogger<DeletionService> _logger = Substitute.For<ILogger<DeletionService>>();
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

            _supportedProfiles.GetProfilesTypes().Returns(new HashSet<string>() { "ValueSet", "StructureDefinition", "CodeSystem" });
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(ci => ci.Arg<DataActions>());

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
                _supportedProfiles,
                _authorizationService,
                _logger);
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
        public async Task GivenConditionalDeleteOfOrdinaryType_WhenIncludeResultIsAProfileResourceAndCallerLacksEditProfileDefinitions_ThenThrowsAndDeletesNothing()
        {
            // Arrange: deleting an ordinary "Provenance" type, but _include pulls in a protected
            // StructureDefinition as part of the same page of results.
            var request = new ConditionalDeleteResourceRequest(
                "Provenance",
                new List<Tuple<string, string>> { Tuple.Create("_include", "Provenance:target") },
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: false);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            var entries = new List<SearchResultEntry>
            {
                CreateSearchResultEntry("Provenance", "prov-1", SearchEntryMode.Match),
                CreateSearchResultEntry("StructureDefinition", "sd-1", SearchEntryMode.Include),
            };

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

            // Caller has normal delete rights but not EditProfileDefinitions.
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.None);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _service.DeleteMultipleAsync(request, CancellationToken.None));

            await fhirDataStore.DidNotReceiveWithAnyArgs().HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenConditionalDeleteOfOrdinaryType_WhenIncludeResultIsAProfileResourceAndCallerHasEditProfileDefinitions_ThenDeletesSucceed()
        {
            // Arrange: same scenario as above, but the caller has EditProfileDefinitions.
            var request = new ConditionalDeleteResourceRequest(
                "Provenance",
                new List<Tuple<string, string>> { Tuple.Create("_include", "Provenance:target") },
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: false);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            var entries = new List<SearchResultEntry>
            {
                CreateSearchResultEntry("Provenance", "prov-1", SearchEntryMode.Match),
                CreateSearchResultEntry("StructureDefinition", "sd-1", SearchEntryMode.Include),
            };

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

            // Default constructor setup grants whatever DataActions are requested, including EditProfileDefinitions.

            // Act
            var result = await _service.DeleteMultipleAsync(request, CancellationToken.None);

            // Assert
            Assert.Equal(2, result.Values.Sum());
            await fhirDataStore.Received(2).HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenConditionalDeleteSpanningMultiplePages_WhenALaterPageContainsAProfileResourceAndCallerLacksEditProfileDefinitions_ThenThrowsUnauthorizedFhirActionExceptionDirectly()
        {
            // Arrange: page 1 is entirely ordinary resources (legitimately deletable); page 2, reached only
            // through pagination (no _include involved), contains a protected StructureDefinition. The
            // resulting exception must be a clean UnauthorizedFhirActionException, not wrapped in
            // IncompleteOperationException like other mid-loop failures, so it maps to a 403.
            var request = new ConditionalDeleteResourceRequest(
                "Provenance",
                new List<Tuple<string, string>> { Tuple.Create("_lastUpdated", "2000-01-01T00:00:00Z") },
                DeleteOperation.HardDelete,
                maxDeleteCount: 10,
                deleteAll: true);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            var firstPageEntries = new List<SearchResultEntry> { CreateSearchResultEntry("Provenance", "prov-1", SearchEntryMode.Match) };
            var secondPageEntries = new List<SearchResultEntry> { CreateSearchResultEntry("StructureDefinition", "sd-1", SearchEntryMode.Match) };

            searchService.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>(),
                Arg.Any<ResourceVersionType>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()).Returns(
                Task.FromResult(new SearchResult(firstPageEntries, "page-2-token", null, Array.Empty<Tuple<string, string>>())),
                Task.FromResult(new SearchResult(secondPageEntries, null, null, Array.Empty<Tuple<string, string>>())));

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedDataStore = new DeletionServiceScopedDataStore(fhirDataStore);
            _dataStoreFactory.GetScopedDataStore().Returns(scopedDataStore);

            // Caller has normal delete rights but not EditProfileDefinitions. Page 1 never calls this (no
            // protected type present), so it has no bearing on page 1's legitimate deletion below.
            _authorizationService.CheckAccess(Arg.Any<DataActions>(), Arg.Any<CancellationToken>()).Returns(DataActions.None);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedFhirActionException>(() => _service.DeleteMultipleAsync(request, CancellationToken.None));

            // Page 1 had already been queued (and was legitimately authorized) before page 2 was denied, so it
            // must still have been deleted; page 2's protected resource must never have been deleted.
            await fhirDataStore.Received(1).HardDeleteAsync(Arg.Is<ResourceKey>(k => k.ResourceType == "Provenance"), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
            await fhirDataStore.DidNotReceive().HardDeleteAsync(Arg.Is<ResourceKey>(k => k.ResourceType == "StructureDefinition"), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        }

        private static SearchResultEntry CreateSearchResultEntry(string resourceType, string resourceId, SearchEntryMode searchEntryMode)
        {
            var rawJson = $"{{\"resourceType\":\"{resourceType}\",\"id\":\"{resourceId}\"}}";
            var resourceElement = new FhirJsonParser().Parse(rawJson).ToResourceElement();
            var rawResource = new RawResource(rawJson, FhirResourceFormat.Json, isMetaSet: false);
            var resourceRequest = Substitute.For<ResourceRequest>();
            var compartmentIndices = Substitute.For<CompartmentIndices>();
            var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, null, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash");
            return new SearchResultEntry(wrapper, searchEntryMode);
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
                .MarkSearchParameterForDeletionAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
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
                .MarkSearchParameterForDeletionAsync(Arg.Any<RawResource>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
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

            var firstPageDeletesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var hardDeleteCount = 0;
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
                        await firstPageDeletesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                        throw new InvalidOperationException("A transport-level error has occurred when receiving results from the server.");
                    }
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            fhirDataStore.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    if (Interlocked.Increment(ref hardDeleteCount) == firstPageEntries.Count)
                    {
                        firstPageDeletesStarted.TrySetResult();
                    }

                    return Task.CompletedTask;
                });

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
    }
}
