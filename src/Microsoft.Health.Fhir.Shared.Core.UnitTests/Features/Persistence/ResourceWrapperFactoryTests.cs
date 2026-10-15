// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ResourceWrapperFactoryTests
    {
        private readonly IRawResourceFactory _rawResourceFactory;
        private readonly ISearchIndexer _searchIndexer;
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;
        private readonly IClaimsExtractor _claimsExtractor;
        private readonly ICompartmentIndexer _compartmentIndexer;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ResourceWrapperFactory _resourceWrapperFactory;

        private readonly SearchParameterInfo _nameSearchParameterInfo;
        private readonly SearchParameterInfo _addressSearchParameterInfo;
        private readonly SearchParameterInfo _ageSearchParameterInfo;
        private readonly SearchParameterInfo _lastUpdatedSearchParameterInfo;
        private readonly SearchParameterInfo _idSearchParameterInfo;
        private readonly SearchParameterInfo _deceasedSearchParameterInfo;

        public ResourceWrapperFactoryTests()
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

            _nameSearchParameterInfo = new SearchParameterInfo("name", "name", ValueSets.SearchParamType.String, new Uri("https://localhost/searchParameter/name")) { SortStatus = SortParameterStatus.Enabled };
            _addressSearchParameterInfo = new SearchParameterInfo("address-city", "address-city", ValueSets.SearchParamType.String, new Uri("https://localhost/searchParameter/address-city")) { SortStatus = SortParameterStatus.Enabled };
            _ageSearchParameterInfo = new SearchParameterInfo("age", "age", ValueSets.SearchParamType.Number, new Uri("https://localhost/searchParameter/age")) { SortStatus = SortParameterStatus.Supported };
            _deceasedSearchParameterInfo = new SearchParameterInfo("deceased", "deceased", ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Patient-deceased"))
            {
                IsPartiallySupported = true,
            };
            _lastUpdatedSearchParameterInfo = new SearchParameterInfo(KnownQueryParameterNames.LastUpdated, KnownQueryParameterNames.LastUpdated, ValueSets.SearchParamType.Date, new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated"));
            _idSearchParameterInfo = new SearchParameterInfo(KnownQueryParameterNames.Id, KnownQueryParameterNames.Id, ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        }

        [Fact]
        public void GivenMultipleStringSearchValueForOneParameter_WhenCreate_ThenMinMaxValuesSetCorrectly()
        {
            var searchIndexEntry1 = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("alpha"));
            var searchIndexEntry2 = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("beta"));
            var searchIndexEntry3 = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("gamma"));
            _searchIndexer
                .Extract(Arg.Any<ResourceElement>())
                .Returns(new List<SearchIndexEntry>() { searchIndexEntry1, searchIndexEntry2, searchIndexEntry3 });

            ResourceElement resource = Samples.GetDefaultPatient(); // Resource does not matter for this test.
            ResourceWrapper resourceWrapper = _resourceWrapperFactory.Create(resource, deleted: false, keepMeta: false);

            foreach (SearchIndexEntry searchEntry in resourceWrapper.SearchIndices)
            {
                ISupportSortSearchValue searchEntryValue = searchEntry.Value as ISupportSortSearchValue;
                switch (searchEntry.Value.ToString())
                {
                    case "alpha":
                        Assert.True(searchEntryValue.IsMin);
                        Assert.False(searchEntryValue.IsMax);
                        break;
                    case "beta":
                        Assert.False(searchEntryValue.IsMin);
                        Assert.False(searchEntryValue.IsMax);
                        break;
                    case "gamma":
                        Assert.False(searchEntryValue.IsMin);
                        Assert.True(searchEntryValue.IsMax);
                        break;
                    default:
                        throw new Exception("Unexpected value");
                }
            }
        }

        [Fact]
        public void GivenOneStringSearchValueForEachParameter_WhenCreate_ThenBothMinMaxSetToTrue()
        {
            var searchIndexEntry1 = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("alpha"));
            var searchIndexEntry2 = new SearchIndexEntry(_addressSearchParameterInfo, new StringSearchValue("redmond"));
            _searchIndexer
                .Extract(Arg.Any<ResourceElement>())
                .Returns(new List<SearchIndexEntry>() { searchIndexEntry1, searchIndexEntry2});

            ResourceElement resource = Samples.GetDefaultPatient(); // Resource does not matter for this test.
            ResourceWrapper resourceWrapper = _resourceWrapperFactory.Create(resource, deleted: false, keepMeta: false);

            foreach (SearchIndexEntry searchEntry in resourceWrapper.SearchIndices)
            {
                ISupportSortSearchValue searchEntryValue = searchEntry.Value as ISupportSortSearchValue;
                switch (searchEntry.Value.ToString())
                {
                    case "alpha":
                        Assert.True(searchEntryValue.IsMin);
                        Assert.True(searchEntryValue.IsMax);
                        break;
                    case "redmond":
                        Assert.True(searchEntryValue.IsMin);
                        Assert.True(searchEntryValue.IsMax);
                        break;
                    default:
                        throw new Exception("Unexpected value");
                }
            }
        }

        [Fact]
        public void GivenDeletedResource_WhenCreate_ThenOnlyBasicSearchIndicesAreExtracted()
        {
            _searchIndexer
                .Extract(Arg.Any<ResourceElement>())
                .Returns(new List<SearchIndexEntry>()
                {
                    new SearchIndexEntry(_deceasedSearchParameterInfo, new TokenSearchValue(null, "false", null)),
                    new SearchIndexEntry(_lastUpdatedSearchParameterInfo, new DateTimeSearchValue(DateTimeOffset.UtcNow)),
                    new SearchIndexEntry(_idSearchParameterInfo, new TokenSearchValue(null, "123", null)),
                });
            _compartmentIndexer
                .Extract(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<SearchIndexEntry>>())
                .Returns(new CompartmentIndices());

            ResourceWrapper resourceWrapper = _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: true, keepMeta: false);

            Assert.Equal(2, resourceWrapper.SearchIndices.Count);
            Assert.True(resourceWrapper.SearchIndices.All(x => x.SearchParameter.Code == KnownQueryParameterNames.LastUpdated || x.SearchParameter.Code == KnownQueryParameterNames.Id));
        }

        [Fact]
        public void GivenDeletedResource_WhenUpdate_ThenSearchIndicesAreCleared()
        {
            var existingSearchIndices = new List<SearchIndexEntry>()
            {
                new SearchIndexEntry(_deceasedSearchParameterInfo, new TokenSearchValue(null, "false", null)),
                new SearchIndexEntry(_lastUpdatedSearchParameterInfo, new DateTimeSearchValue(DateTimeOffset.UtcNow)),
                new SearchIndexEntry(_idSearchParameterInfo, new TokenSearchValue(null, "123", null)),
            };
            ResourceElement resource = Samples.GetDefaultPatient();
            ResourceWrapper resourceWrapper = new ResourceWrapper(
                resource,
                _rawResourceFactory.Create(resource, keepMeta: false),
                new ResourceRequest("DELETE"),
                deleted: true,
                existingSearchIndices,
                new CompartmentIndices(),
                Array.Empty<KeyValuePair<string, string>>(),
                searchParameterHash: "hash");

            _resourceWrapperFactory.Update(resourceWrapper);

            Assert.Equal(2, resourceWrapper.SearchIndices.Count);
            Assert.True(resourceWrapper.SearchIndices.All(x => x.SearchParameter.Code == KnownQueryParameterNames.LastUpdated || x.SearchParameter.Code == KnownQueryParameterNames.Id));
        }

        [Fact]
        public void GivenCurrentSearchParameters_WhenUpdate_ThenSearchIndicesAndHashAreUpdated()
        {
            var initialSearchIndexEntry = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("alpha"));
            var updatedSearchIndexEntry = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("beta"));
            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new[] { initialSearchIndexEntry });

            ResourceWrapper resourceWrapper = _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false);

            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new[] { updatedSearchIndexEntry });
            _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient").Returns("currentHash");

            _resourceWrapperFactory.Update(resourceWrapper);

            Assert.Same(updatedSearchIndexEntry, Assert.Single(resourceWrapper.SearchIndices));
            Assert.Equal("currentHash", resourceWrapper.SearchParameterHash);
        }

        [Fact]
        public async Task GivenResources_WhenUpdateAsync_ThenOrdinaryIndicesPrecedeOneAwaitedVectorBatch()
        {
            var resources = new[]
            {
                _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false),
                _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false),
            };
            var updatedIndex = new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("updated"));
            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(new[] { updatedIndex });
            _searchIndexer.ClearReceivedCalls();
            _searchParameterDefinitionManager.GetSearchParameterHashForResourceType("Patient").Returns("updatedHash");
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            vectorIndexer.UpdateVectorSearchIndicesAsync(resources, cancellation.Token).Returns(_ =>
            {
                _searchIndexer.Received(2).Extract(Arg.Any<ResourceElement>());
                Assert.All(resources, resource =>
                {
                    Assert.Same(updatedIndex, Assert.Single(resource.SearchIndices));
                    Assert.Equal("updatedHash", resource.SearchParameterHash);
                });
                var value = Assert.IsType<StringSearchValue>(updatedIndex.Value);
                Assert.True(value.IsMin);
                Assert.True(value.IsMax);
                return completion.Task;
            });

            Task update = CreateFactory(vectorIndexer).UpdateAsync(resources, cancellation.Token);

            Assert.False(update.IsCompleted);
            completion.SetResult();
            await update;
            await vectorIndexer.Received(1).UpdateVectorSearchIndicesAsync(resources, cancellation.Token);
        }

        [Fact]
        public async Task GivenPreparedResource_WhenCompletingVectors_ThenOrdinaryIndicesAndVersionArePreserved()
        {
            var resource = _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false);
            resource.Version = "42";
            var indices = resource.SearchIndices;
            var resources = new[] { resource };
            _searchIndexer.ClearReceivedCalls();
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();

            await CreateFactory(vectorIndexer).UpdateVectorSearchIndicesAsync(resources, CancellationToken.None);

            _searchIndexer.DidNotReceive().Extract(Arg.Any<ResourceElement>());
            Assert.Same(indices, resource.SearchIndices);
            Assert.Equal("42", resource.Version);
            await vectorIndexer.Received(1).UpdateVectorSearchIndicesAsync(resources, CancellationToken.None);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenVectorIndexingIsDisabled_WhenUpdating_ThenVectorStateRemainsUnevaluated(bool deleted)
        {
            var resource = _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted, keepMeta: false);
            var resources = new[] { resource };

            await _resourceWrapperFactory.UpdateAsync(resources, CancellationToken.None);
            await _resourceWrapperFactory.UpdateVectorSearchIndicesAsync(resources, CancellationToken.None);

            Assert.False(resource.VectorSearchIndicesUpdated);
        }

        [Fact]
        public void GivenVectorIndexingIsEnabled_WhenCreatingAndUpdatingSynchronously_ThenVectorsAreNotEvaluated()
        {
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();
            var factory = CreateFactory(vectorIndexer);

            var resource = factory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false);
            factory.Update(resource);

            Assert.False(resource.VectorSearchIndicesUpdated);
            Assert.Empty(vectorIndexer.ReceivedCalls());
        }

        [Fact]
        public async Task GivenDeletedResource_WhenUpdatingAsync_ThenBasicIndicesArePreservedBeforeVectorCleanup()
        {
            var resource = _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: true, keepMeta: false);
            resource.UpdateSearchIndices(new[]
            {
                new SearchIndexEntry(_nameSearchParameterInfo, new StringSearchValue("remove")),
                new SearchIndexEntry(_idSearchParameterInfo, new TokenSearchValue(null, "123", null)),
            });
            _searchIndexer.ClearReceivedCalls();
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();
            var resources = new[] { resource };
            vectorIndexer.UpdateVectorSearchIndicesAsync(resources, CancellationToken.None).Returns(_ =>
            {
                Assert.Same(_idSearchParameterInfo, Assert.Single(resource.SearchIndices).SearchParameter);
                resource.UpdateVectorSearchIndices(Array.Empty<VectorSearchIndexEntry>());
                return Task.CompletedTask;
            });

            await CreateFactory(vectorIndexer).UpdateAsync(resources, CancellationToken.None);

            _searchIndexer.DidNotReceive().Extract(Arg.Any<ResourceElement>());
            Assert.True(resource.VectorSearchIndicesUpdated);
            Assert.Empty(resource.VectorSearchIndices);
            await vectorIndexer.Received(1).UpdateVectorSearchIndicesAsync(resources, CancellationToken.None);
        }

        [Fact]
        public async Task GivenEmptyBatch_WhenUpdatingAsync_ThenNoIndexersAreInvoked()
        {
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();
            var factory = CreateFactory(vectorIndexer);

            await factory.UpdateAsync(Array.Empty<ResourceWrapper>(), CancellationToken.None);
            await factory.UpdateVectorSearchIndicesAsync(Array.Empty<ResourceWrapper>(), CancellationToken.None);

            _searchIndexer.DidNotReceive().Extract(Arg.Any<ResourceElement>());
            Assert.Empty(vectorIndexer.ReceivedCalls());
        }

        [Fact]
        public async Task GivenCancelledToken_WhenUpdatingAsync_ThenNoIndexersAreInvoked()
        {
            var resources = new[] { _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false) };
            _searchIndexer.ClearReceivedCalls();
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();
            var factory = CreateFactory(vectorIndexer);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.UpdateAsync(resources, cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.UpdateVectorSearchIndicesAsync(resources, cancellation.Token));

            _searchIndexer.DidNotReceive().Extract(Arg.Any<ResourceElement>());
            Assert.Empty(vectorIndexer.ReceivedCalls());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenVectorFailure_WhenUpdatingAsync_ThenFailurePropagates(bool cancelled)
        {
            var resources = new[] { _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false) };
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();
            using var cancellation = new CancellationTokenSource();
            var failure = cancelled ? (Exception)new OperationCanceledException(cancellation.Token) : new InvalidOperationException("embedding failed");
            vectorIndexer.UpdateVectorSearchIndicesAsync(resources, cancellation.Token).Returns(Task.FromException(failure));
            var factory = CreateFactory(vectorIndexer);

            Exception actual = await Record.ExceptionAsync(() => factory.UpdateAsync(resources, cancellation.Token));

            Assert.Same(failure, actual);
            await vectorIndexer.Received(1).UpdateVectorSearchIndicesAsync(resources, cancellation.Token);
        }

        [Fact]
        public async Task GivenOrdinaryExtractionFailure_WhenUpdatingAsync_ThenVectorIndexingDoesNotStart()
        {
            var resources = new[] { _resourceWrapperFactory.Create(Samples.GetDefaultPatient(), deleted: false, keepMeta: false) };
            var failure = new InvalidOperationException("extraction failed");
            _searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(_ => throw failure);
            var vectorIndexer = Substitute.For<IVectorSearchIndexer>();

            Exception actual = await Record.ExceptionAsync(() => CreateFactory(vectorIndexer).UpdateAsync(resources, CancellationToken.None));

            Assert.Same(failure, actual);
            Assert.Empty(vectorIndexer.ReceivedCalls());
        }

        private ResourceWrapperFactory CreateFactory(IVectorSearchIndexer vectorIndexer)
        {
            return new ResourceWrapperFactory(
                _rawResourceFactory,
                _fhirRequestContextAccessor,
                _searchIndexer,
                _claimsExtractor,
                _compartmentIndexer,
                _searchParameterDefinitionManager,
                Deserializers.ResourceDeserializer,
                vectorIndexer);
        }
    }
}
