// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Upsert
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    public class BulkUpdateServiceTests
    {
        private readonly Lazy<IConformanceProvider> _conformanceProvider = new Lazy<IConformanceProvider>(() => Substitute.For<IConformanceProvider>());
        private readonly IScopeProvider<IFhirDataStore> _fhirDataStoreFactory = Substitute.For<IScopeProvider<IFhirDataStore>>();
        private readonly IScopeProvider<ISearchService> _searchServiceFactory = Substitute.For<IScopeProvider<ISearchService>>();
        private readonly ResourceIdProvider _resourceIdProvider = Substitute.For<ResourceIdProvider>();
        private readonly FhirRequestContextAccessor _contextAccessor = Substitute.For<FhirRequestContextAccessor>();
        private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
        private readonly ILogger<BulkUpdateService> _logger = Substitute.For<ILogger<BulkUpdateService>>();
        private readonly ResourceDeserializer _resourceDeserializer = Deserializers.ResourceDeserializer;
        private readonly BulkUpdateService _service;

        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly ISearchIndexer _searchIndexer;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ICompartmentIndexer _compartmentIndexer;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ResourceWrapperFactory _resourceWrapperFactory;

        private readonly string fhirPatchParameters = "{\"resourceType\":\"Parameters\",\"parameter\":[{\"name\":\"operation\",\"part\":[{\"name\":\"type\",\"valueCode\":\"upsert\"},{\"name\":\"path\",\"valueString\":\"Patient\"},{\"name\":\"name\",\"valueString\":\"language\"},{\"name\":\"value\",\"valueCode\":\"en\"}]},{\"name\":\"operation\",\"part\":[{\"name\":\"type\",\"valueCode\":\"upsert\"},{\"name\":\"path\",\"valueString\":\"Organization\"},{\"name\":\"name\",\"valueString\":\"active\"},{\"name\":\"value\",\"valueBoolean\":\"true\"}]},{\"name\":\"operation\",\"part\":[{\"name\":\"type\",\"valueCode\":\"upsert\"},{\"name\":\"path\",\"valueString\":\"Practitioner\"},{\"name\":\"name\",\"valueString\":\"id\"},{\"name\":\"value\",\"valueString\":\"test\"}]}]}";

        public BulkUpdateServiceTests()
        {
            var serializer = new FhirJsonSerializer();
            _rawResourceFactory = new RawResourceFactory(serializer);

            var dummyRequestContext = new FhirRequestContext(
                "POST",
                "https://localhost/Patient",
                "https://localhost/",
                Guid.NewGuid().ToString(),
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>());
            _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            _fhirRequestContextAccessor.RequestContext.Returns(dummyRequestContext);

            _claimsExtractor = Substitute.For<IClaimsExtractor>();
            _compartmentIndexer = Substitute.For<ICompartmentIndexer>();
            _searchIndexer = Substitute.For<ISearchIndexer>();

            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            _searchParameterDefinitionManager.GetSearchParameterHashForResourceType(Arg.Any<string>()).Returns("hash");

            _resourceWrapperFactory = new ResourceWrapperFactory(
                _rawResourceFactory,
                _fhirRequestContextAccessor,
                _searchIndexer,
                _claimsExtractor,
                _compartmentIndexer,
                _searchParameterDefinitionManager,
                Deserializers.ResourceDeserializer);

            _service = new BulkUpdateService(
                _resourceWrapperFactory,
                _conformanceProvider,
                _fhirDataStoreFactory,
                _searchServiceFactory,
                _resourceIdProvider,
                _contextAccessor,
                _auditLogger,
                _logger);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public async Task UpdateMultipleAsync_WhenNoResults_ReturnsEmptyBulkUpdateResult(uint readUpto)
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;
            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            searchService.SearchAsync(
                resourceType,
                conditionalParameters,
                cancellationToken,
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns(new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, Array.Empty<Tuple<string, string>>()));

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, readUpto, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result.ResourcesUpdated);
            Assert.Empty(result.ResourcesIgnored);
            Assert.Empty(result.ResourcesPatchFailed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task UpdateMultipleAsync_WhenResultsReturned_ResourcesAreUpdated(uint readUpto)
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;
            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);
            searchService.SearchAsync(
                resourceType,
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                Arg.Any<CancellationToken>(),
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 } }, "continuationToken"));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, readUpto, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);
            uint timesFactor = readUpto == 0 ? 1 : readUpto;

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.ResourcesUpdated); // Should be updated or empty if patch fails
            Assert.True(result.ResourcesUpdated["Patient"] == 5 * timesFactor); // Assuming all 5 resources were updated successfully
            Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2 * timesFactor); // Practitioner failed on immutable property update
            Assert.True(result.ResourcesIgnored["Observation"] == 1 * timesFactor); // Observations ignored as no applicable patch request
        }

        [Fact]
        public async Task UpdateMultipleAsync_WhenResultsReturnedWithHistoricalRecords_OnlyLatestResourcesAreUpdated()
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;
            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);
            searchService.SearchAsync(
                resourceType,
                conditionalParameters,
                cancellationToken,
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns((x) =>
                {
                    return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 } }, null, null, null, new Dictionary<string, int> { { "Patient", 2 }, { "Observation", 1 }, { "Practitioner", 1 } }));
                });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.Single(result.ResourcesUpdated); // Should be updated or empty if patch fails
            Assert.True(result.ResourcesUpdated["Patient"] == 5); // Assuming all 5 resources were updated successfully
            Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2); // Practitioner failed on immutable property update
            Assert.True(result.ResourcesIgnored["Observation"] == 1); // Observations ignored as no applicable patch request
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(true, 5)]
        [InlineData(true, 10)]
        [InlineData(false, 0)]
        [InlineData(false, 1)]
        [InlineData(false, 5)]
        [InlineData(false, 10)]
        public async Task UpdateMultipleAsync_WhenSingleMatchAndZeroIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage, uint readUpto)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            searchService.SearchAsync(
                resourceType,
                Arg.Any<List<Tuple<string, string>>>(),
                cancellationToken,
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns((x) =>
                {
                    return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 2 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, null));
                });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, readUpto, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            // readUpto is ignored when readNextPage is true
            // readUpto is considered when readNextPage is false but since there is only one matched page, it is ignored
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2); // Should be updated or empty if patch fails
            Assert.True(result.ResourcesUpdated["Patient"] == 2); // Assuming all 2 resources were updated successfully
            Assert.True(result.ResourcesUpdated["Organization"] == 2); // Assuming all 2 resources were updated successfully
            Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2); // Practitioner failed on immutable property update
            Assert.True(result.ResourcesIgnored["Observation"] == 1); // Observations ignored as no applicable patch request
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(true, 5)]
        [InlineData(true, 10)]
        [InlineData(false, 0)]
        [InlineData(false, 1)]
        [InlineData(false, 5)]
        [InlineData(false, 10)]
        public async Task UpdateMultipleAsync_WhenSingleMatchAndSingleIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage, uint readUpto)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, null, "includesContinuationToken"));
            });

            // Simulate a include search result
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, null));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, readUpto, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            // readUpto is ignored when readNextPage is true
            // readUpto is considered when readNextPage is false but since there is only one matched page, it is ignored
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2); // Should be updated or empty if patch fails
            Assert.True(result.ResourcesUpdated["Patient"] == 5); // Assuming all 5 resources were updated successfully
            Assert.True(result.ResourcesUpdated["Organization"] == 4); // Assuming all 4 resources were updated successfully
            Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2); // Practitioner failed on immutable property update
            Assert.True(result.ResourcesIgnored["Observation"] == 1); // Observations ignored as no applicable patch request
        }

        [Theory]
        [InlineData(true, 0)]
        [InlineData(true, 1)]
        [InlineData(true, 5)]
        [InlineData(true, 10)]
        [InlineData(false, 0)]
        [InlineData(false, 1)]
        [InlineData(false, 5)]
        [InlineData(false, 10)]
        public async Task UpdateMultipleAsync_WhenSingleMatchAndMultipleIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage, uint readUpto)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, null, "includesContinuationToken"));
            });

            // Simulate a include search result
            int callCountForIncludeResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
            {
                callCountForIncludeResults++;
                var includesContinuationToken = callCountForIncludeResults <= 2 ? "includesContinuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, includesContinuationToken));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, readUpto, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            // readUpto is ignored when readNextPage is true
            // readUpto is considered when readNextPage is false but since there is only one matched page, it is ignored
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2); // Should be updated or empty if patch fails
            Assert.True(result.ResourcesUpdated["Patient"] == 5); // Assuming all 5 resources were updated successfully
            Assert.True(result.ResourcesUpdated["Organization"] == 8); // Assuming all 8 resources were updated successfully
            Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2); // Practitioner failed on immutable property update
            Assert.True(result.ResourcesIgnored["Observation"] == 1); // Observations ignored as no applicable patch request
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UpdateMultipleAsync_WhenSingleMatchAndMoreThanMaxParallelThreadsIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, null, "includesContinuationToken"));
            });

            // Simulate a include search result
            int callCountForIncludeResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
            {
                callCountForIncludeResults++;
                var includesContinuationToken = callCountForIncludeResults <= 80 ? "includesContinuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, includesContinuationToken));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2);
            Assert.True(result.ResourcesUpdated["Patient"] == 5);
            Assert.True(result.ResourcesUpdated["Organization"] == 164);
            Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2);
            Assert.True(result.ResourcesIgnored["Observation"] == 1);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UpdateMultipleAsync_WhenMultipleMatchAndZeroIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            int callCountForMatchedResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                callCountForMatchedResults++;
                var continuationToken = callCountForMatchedResults <= 2 ? "continuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, continuationToken, null));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2); // Should be updated or empty if patch fails

            if (readNextPage)
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 15);
                Assert.True(result.ResourcesUpdated["Organization"] == 6);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 6);
                Assert.True(result.ResourcesIgnored["Observation"] == 3);
            }
            else
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 5);
                Assert.True(result.ResourcesUpdated["Organization"] == 2);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2);
                Assert.True(result.ResourcesIgnored["Observation"] == 1);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UpdateMultipleAsync_WhenMultipleMatchAndSingleIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            int callCountForMatchedResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                callCountForMatchedResults++;
                var continuationToken = callCountForMatchedResults <= 2 ? "continuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, continuationToken, "includesContinuationToken"));
            });

            // Simulate a include search result
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
            {
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, null));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2);
            if (readNextPage)
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 15);
                Assert.True(result.ResourcesUpdated["Organization"] == 12);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 6);
                Assert.True(result.ResourcesIgnored["Observation"] == 3);
            }
            else
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 5);
                Assert.True(result.ResourcesUpdated["Organization"] == 4);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2);
                Assert.True(result.ResourcesIgnored["Observation"] == 1);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UpdateMultipleAsync__WhenMultipleMatchAndMultipleIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            int callCountForMatchedResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                callCountForMatchedResults++;
                var continuationToken = callCountForMatchedResults <= 2 ? "continuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, continuationToken, "includesContinuationToken"));
            });

            // Simulate a include search result
            int callCountForIncludeResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
            {
                callCountForIncludeResults++;
                var includesContinuationToken = callCountForIncludeResults <= 2 ? "includesContinuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, includesContinuationToken));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2);

            if (readNextPage)
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 15);
                Assert.True(result.ResourcesUpdated["Organization"] == 16);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 6);
                Assert.True(result.ResourcesIgnored["Observation"] == 3);
            }
            else
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 5);
                Assert.True(result.ResourcesUpdated["Organization"] == 8);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 2);
                Assert.True(result.ResourcesIgnored["Observation"] == 1);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task UpdateMultipleAsync__WhenMoreThanMaxParallelThreadsMatchAndIncludePagesWithGivenReadNextPage_ResourcesAreUpdated(bool readNextPage)
        {
            // Arrange
            var resourceType = "Patient";
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            int callCountForMatchedResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
            {
                callCountForMatchedResults++;
                var continuationToken = callCountForMatchedResults <= 80 ? "continuationToken" : null;
                var includesContinuationToken = callCountForMatchedResults <= 2 ? "includesContinuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 1 }, { "Observation", 1 }, { "Practitioner", 1 }, { "Organization", 2 } }, continuationToken, includesContinuationToken));
            });

            // Simulate a include search result
            int callCountForIncludeResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
            {
                callCountForIncludeResults++;
                var includesContinuationToken = callCountForIncludeResults <= 80 ? "includesContinuationToken" : null;
                return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, includesContinuationToken));
            });

            // Act
            var result = await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.ResourcesUpdated.Count == 2);

            if (readNextPage)
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 81);
                Assert.True(result.ResourcesUpdated["Organization"] == 326);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 81);
                Assert.True(result.ResourcesIgnored["Observation"] == 81);
            }
            else
            {
                Assert.True(result.ResourcesUpdated["Patient"] == 1);
                Assert.True(result.ResourcesUpdated["Organization"] == 164);
                Assert.True(result.ResourcesPatchFailed["Practitioner"] == 1);
                Assert.True(result.ResourcesIgnored["Observation"] == 1);
            }
        }

        [Fact]
        public async Task UpdateMultipleAsync_WhenMergeAsyncThrowsIncompleteOperationException_ThrowsIncompleteOperationExceptionBulkUpdateResult()
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            // Set up the BundleIssues to trigger AreIncludeResultsTruncated
            var bundleIssues = new List<OperationOutcomeIssue>
            {
                new OperationOutcomeIssue("warning", "informational", "Included items are truncated. Use the related link in the response to retrieve all included items."),
            };
            var mockRequestContext = Substitute.For<IFhirRequestContext>();
            mockRequestContext.BundleIssues.Returns(bundleIssues);
            _contextAccessor.RequestContext.Returns(mockRequestContext);

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), isIncludesRequest).Returns((x) =>
                {
                    return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 5 }, { "Observation", 1 }, { "Practitioner", 2 }, { "Organization", 2 } }, "continuationToken", "includesContinuationToken"));
                });

            // Simulate a include search result
            int callCountForIncludeResults = 0;
            searchService.SearchAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>(), Arg.Any<ResourceVersionType>(), Arg.Any<bool>(), !isIncludesRequest).Returns((x) =>
                {
                    callCountForIncludeResults++;
                    var includesContinuationToken = callCountForIncludeResults <= 2 ? "includesContinuationToken" : null;
                    return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Organization", 2 } }, null, includesContinuationToken));
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedFhirDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            scopedFhirDataStore.Value.Returns(fhirDataStore);
            _fhirDataStoreFactory.Invoke().Returns(scopedFhirDataStore);

            // Simulate MergeAsync throwing IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>
            var partialResults = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>
            {
                { new DataStoreOperationIdentifier("id1", "Patient", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Partial error")) },
                { new DataStoreOperationIdentifier("id2", "Patient", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Partial error")) },
                { new DataStoreOperationIdentifier("id3", "Patient", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Partial error")) },
                { new DataStoreOperationIdentifier("id1", "Organization", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Partial error")) },
                { new DataStoreOperationIdentifier("id2", "Organization", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Partial error")) },
            };
            var innerException = new InvalidOperationException("Simulated inner error");
            int mergeCallCount = 0;
            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    mergeCallCount++;
                    if (mergeCallCount <= 4)
                    {
                        // Return a successful result (simulate success)
                        var successResult = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>
                        {
                            { new DataStoreOperationIdentifier("OrganizationAId" + mergeCallCount, "Organization", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Success")) },
                            { new DataStoreOperationIdentifier("OrganizationBId" + mergeCallCount, "Organization", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Success")) },
                        };
                        return Task.FromResult<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>(successResult);
                    }
                    else
                    {
                        // Return the exception as before
                        return Task.FromException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>(
                            new IncompleteOperationException<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>(innerException, partialResults));
                    }
                });

            // Act & Assert
            var ex = await Assert.ThrowsAsync<IncompleteOperationException<BulkUpdateResult>>(async () =>
            {
                await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);
            });

            // The inner exception should be an AggregateException containing the simulated failure
            Assert.NotNull(ex.InnerException);
            Assert.IsType<AggregateException>(ex.InnerException);
            var aggEx = (AggregateException)ex.InnerException;
            Assert.Contains(aggEx.InnerExceptions, e =>
                e is IncompleteOperationException<BulkUpdateResult> incomplete &&
                incomplete.InnerException is InvalidOperationException &&
                incomplete.InnerException.Message == "Simulated inner error");

            // The partial results should be present
            Assert.NotNull(ex.PartialResults);
            Assert.True(ex.PartialResults.ResourcesUpdated.Count == 2);
            Assert.True(ex.PartialResults.ResourcesIgnored.Count == 1);
            Assert.True(ex.PartialResults.ResourcesPatchFailed.Count == 1);
            Assert.True(ex.PartialResults.ResourcesUpdated["Patient"] == 3);
            Assert.True(ex.PartialResults.ResourcesUpdated["Organization"] == 8);
            Assert.True(ex.PartialResults.ResourcesIgnored["Observation"] == 1);
            Assert.True(ex.PartialResults.ResourcesPatchFailed["Practitioner"] == 2);
        }

        [Fact]
        public async Task UpdateMultipleAsync_WhenOperationTimesout_ReturnsPartialResults()
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            using var cancellationTokenSource = new CancellationTokenSource();

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate two resources, but cancel after the first update
            int updateCount = 0;
            searchService.SearchAsync(
                resourceType,
                conditionalParameters,
                Arg.Any<CancellationToken>(),
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns((x) =>
                {
                    return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 2 }, { "Observation", 1 }, { "Practitioner", 2 } }));
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedFhirDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            scopedFhirDataStore.Value.Returns(fhirDataStore);
            _fhirDataStoreFactory.Invoke().Returns(scopedFhirDataStore);

            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    updateCount++;
                    if (updateCount == 1)
                    {
                        throw new TimeoutException();
                    }

                    return Task.FromResult<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>(new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>
                    {
                        { new DataStoreOperationIdentifier(Guid.NewGuid().ToString(), "Patient", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Error message")) },
                    });
                });

            // Act & Assert
            var ex = await Assert.ThrowsAsync<IncompleteOperationException<BulkUpdateResult>>(async () =>
            {
                await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationTokenSource.Token);
            });

            Assert.Equal(1, updateCount);

            // Check for partial results
            Assert.NotNull(ex.PartialResults);
            Assert.True(ex.PartialResults.ResourcesUpdated.Count == 0); // No partial updates as it was the timeout exception
            Assert.True(ex.PartialResults.ResourcesIgnored.Count == 1);
            Assert.True(ex.PartialResults.ResourcesPatchFailed.Count == 1);
            Assert.True(ex.PartialResults.ResourcesPatchFailed["Practitioner"] == 2); // Practitioner failed on immutable property update
            Assert.True(ex.PartialResults.ResourcesIgnored["Observation"] == 1); // Observations ignored as no applicable patch request
        }

        [Fact]
        public async Task UpdateMultipleAsync_WhenUpdateTaskFaults_ThrowsAggregateException()
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            var cancellationToken = CancellationToken.None;

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate a search result with one resource
            searchService.SearchAsync(
                resourceType,
                conditionalParameters,
                cancellationToken,
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns(Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 1 } })));

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedFhirDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            scopedFhirDataStore.Value.Returns(fhirDataStore);
            _fhirDataStoreFactory.Invoke().Returns(scopedFhirDataStore);

            // Simulate MergeAsync throwing an exception
            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns<Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>>(callInfo =>
                {
                    throw new InvalidOperationException("Simulated failure");
                });

            // Act & Assert
            var ex = await Assert.ThrowsAsync<IncompleteOperationException<BulkUpdateResult>>(async () =>
            {
                await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationToken);
            });

            // The inner exception should be an AggregateException containing the simulated failure
            Assert.NotNull(ex.InnerException);
            Assert.IsType<AggregateException>(ex.InnerException);
            var aggEx = (AggregateException)ex.InnerException;
            Assert.Contains(aggEx.InnerExceptions, e => e is InvalidOperationException && e.Message == "Simulated failure");
        }

        [Fact]
        public async Task UpdateMultipleAsync_WhenCancelled_ReturnsPartialResults()
        {
            // Arrange
            var resourceType = "Patient";
            var readNextPage = false;
            var isIncludesRequest = false;
            var conditionalParameters = new List<Tuple<string, string>>();
            using var cancellationTokenSource = new CancellationTokenSource();

            var searchService = Substitute.For<ISearchService>();
            var scopedSearchService = Substitute.For<IScoped<ISearchService>>();
            scopedSearchService.Value.Returns(searchService);
            _searchServiceFactory.Invoke().Returns(scopedSearchService);

            // Simulate two resources, but cancel after the first update
            int updateCount = 0;
            searchService.SearchAsync(
                resourceType,
                conditionalParameters,
                Arg.Any<CancellationToken>(),
                true,
                ResourceVersionType.Latest,
                false,
                isIncludesRequest).Returns((x) =>
                {
                    return Task.FromResult(GenerateSearchResult(new Dictionary<string, int> { { "Patient", 2 }, { "Observation", 1 }, { "Practitioner", 2 } }));
                });

            var fhirDataStore = Substitute.For<IFhirDataStore>();
            var scopedFhirDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            scopedFhirDataStore.Value.Returns(fhirDataStore);
            _fhirDataStoreFactory.Invoke().Returns(scopedFhirDataStore);

            fhirDataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    updateCount++;
                    var token = callInfo.Arg<CancellationToken>();
                    if (updateCount == 1)
                    {
                        cancellationTokenSource.Cancel();
                    }

                    if (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }

                    return Task.FromResult<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>>(new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>
                    {
                        { new DataStoreOperationIdentifier(Guid.NewGuid().ToString(), "Patient", "1", true, false, null, false), new DataStoreOperationOutcome(new MicrosoftHealthException("Error message")) },
                    });
                });

            // Act & Assert
            var ex = await Assert.ThrowsAsync<IncompleteOperationException<BulkUpdateResult>>(async () =>
            {
                await _service.UpdateMultipleAsync(resourceType, fhirPatchParameters, readNextPage, 0, isIncludesRequest, conditionalParameters, bundleResourceContext: null, cancellationTokenSource.Token);
            });

            Assert.Equal(1, updateCount);
            Assert.True(ex.PartialResults.ResourcesIgnored.Count == 1);
            Assert.True(ex.PartialResults.ResourcesPatchFailed.Count == 1);
            Assert.True(ex.PartialResults.ResourcesPatchFailed["Practitioner"] == 2); // Practitioner failed on immutable property update
            Assert.True(ex.PartialResults.ResourcesIgnored["Observation"] == 1); // Observations ignored as no applicable patch request
        }

        private SearchResult GenerateSearchResult(
            Dictionary<string, int> resourceTypeCounts,
            string continuationToken = null,
            string includesContinuationToken = null,
            IReadOnlyCollection<SearchIndexEntry> searchIndices = null,
            Dictionary<string, int> historicalRecords = null)
        {
            var entries = new List<SearchResultEntry>();

            CreateSearchResults(searchIndices, resourceTypeCounts, entries, false);
            CreateSearchResults(searchIndices, historicalRecords, entries, true);

            var searchResult = new SearchResult(
                entries,
                continuationToken,
                null,
                Array.Empty<Tuple<string, string>>(),
                null,
                includesContinuationToken);

            return searchResult;
        }

        private static void CreateSearchResults(IReadOnlyCollection<SearchIndexEntry> searchIndices, Dictionary<string, int> resourceCounts, List<SearchResultEntry> entries, bool markHistorical = false)
        {
            if (resourceCounts is not null && resourceCounts.Count > 0)
            {
                foreach (var kvp in resourceCounts)
                {
                    var resourceType = kvp.Key;
                    var count = kvp.Value;

                    for (int i = 0; i < count; i++)
                    {
                        Resource resource;
                        switch (resourceType)
                        {
                            case "Patient":
                                resource = Samples.GetDefaultPatient().ToPoco<Patient>();
                                break;
                            case "Observation":
                                resource = Samples.GetDefaultObservation().ToPoco<Observation>();
                                break;
                            case "Practitioner":
                                resource = Samples.GetDefaultPractitioner().ToPoco<Practitioner>();
                                break;
                            case "Organization":
                                resource = Samples.GetDefaultOrganization().ToPoco<Organization>();
                                break;
                            default:
                                throw new ArgumentException($"Unsupported resource type: {resourceType}");
                        }

                        resource.Id = Guid.NewGuid().ToString();
                        resource.VersionId = "1";

                        var resourceElement = resource.ToResourceElement();
                        var rawResource = new RawResource(resource.ToJson(), FhirResourceFormat.Json, isMetaSet: false);
                        var resourceRequest = Substitute.For<ResourceRequest>();
                        var compartmentIndices = Substitute.For<CompartmentIndices>();
                        var wrapper = new ResourceWrapper(resourceElement, rawResource, resourceRequest, false, searchIndices, compartmentIndices, new List<KeyValuePair<string, string>>(), "hash") { IsHistory = markHistorical };
                        var entry = new SearchResultEntry(wrapper, resourceType == "Organization" ? SearchEntryMode.Include : SearchEntryMode.Match);
                        entries.Add(entry);
                    }
                }
            }
        }
    }
}
