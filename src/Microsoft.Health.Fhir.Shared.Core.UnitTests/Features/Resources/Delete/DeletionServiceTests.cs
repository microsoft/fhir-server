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

            var exception = await Assert.ThrowsAsync<IncompleteOperationException<Dictionary<string, long>>>(async () =>
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
            var exception = await Assert.ThrowsAsync<IncompleteOperationException<Dictionary<string, long>>>(async () =>
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
