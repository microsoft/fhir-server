// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosFhirDataStoreTests
    {
        private readonly ICosmosQueryFactory _cosmosQueryFactory;
        private readonly CosmosFhirDataStore _dataStore;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();
        private readonly IScoped<Container> _container;
        private readonly IBundleOrchestrator _bundleOrchestrator;

        public CosmosFhirDataStoreTests()
        {
            _container = Substitute.For<Container>().CreateMockScope();
            _cosmosQueryFactory = Substitute.For<ICosmosQueryFactory>();
            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            fhirRequestContext.ExecutingBatchOrTransaction.Returns(true);
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(fhirRequestContext);

            var bundleConfiguration = new BundleConfiguration() { SupportsBundleOrchestrator = true };
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(bundleConfiguration);

            var logger = Substitute.For<ILogger<BundleOrchestrator>>();

            _bundleOrchestrator = new BundleOrchestrator(bundleOptions, logger);

            _dataStore = new CosmosFhirDataStore(
                _container,
                _cosmosDataStoreConfiguration,
                Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>(),
                _cosmosQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, requestContextAccessor),
                NullLogger<CosmosFhirDataStore>.Instance,
                Options.Create(new CoreFeatureConfiguration()),
                _bundleOrchestrator,
                new Lazy<ISupportedSearchParameterDefinitionManager>(Substitute.For<ISupportedSearchParameterDefinitionManager>()),
                ModelInfoProvider.Instance);
        }

        [Fact]
        public async Task GivenAQuery_WhenASinglePageReturnsRequestedCount_ASingleQueryIsPerformced()
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(default, default).ReturnsForAnyArgs(cosmosQuery);

            FeedResponse<int> response = CreateFeedResponse(0, 10, null);

            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(response);

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 });

            Assert.Equal(Enumerable.Range(0, 10), results);
            Assert.Null(continuationToken);
        }

        [Fact]
        public async Task GivenAQuery_WhenFetchingSubsequentPagesYieldsA429_ReturnsExistingResults()
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(default, default).ReturnsForAnyArgs(cosmosQuery);

            FeedResponse<int> response = CreateFeedResponse(0, 2, "token");

            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(ci => response, ci => throw CreateCosmosException(new RequestRateExceededException(null)));
            cosmosQuery.HasMoreResults.Returns(true);

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 });

            Assert.Equal(Enumerable.Range(0, 2), results);
            Assert.Equal("token", continuationToken);
        }

        [Fact]
        public async Task GivenAQuery_WhenFetchingSubsequentPagesTimesOut_ReturnsExistingResults()
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(default, default).ReturnsForAnyArgs(cosmosQuery);

            FeedResponse<int> response = CreateFeedResponse(0, 2, "token");

            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(ci => response, ci => throw new OperationCanceledException());
            cosmosQuery.HasMoreResults.Returns(true);

            var time = DateTimeOffset.UtcNow;
            _cosmosDataStoreConfiguration.SearchEnumerationTimeoutInSeconds = 0;

            // lock the time
            using (Mock.Property(() => ClockResolver.UtcNowFunc, () => time))
            {
                (IReadOnlyList<int> results, string continuationToken) =
                    await _dataStore.ExecuteDocumentQueryAsync<int>(
                        new QueryDefinition("abc"),
                        new QueryRequestOptions { MaxItemCount = 10 });

                Assert.Equal(Enumerable.Range(0, 2), results);
                Assert.Equal("token", continuationToken);
            }
        }

        [Fact]
        public async Task GivenAQueryWhereItemCountCanBeExceeded_WhenExecuted_FetchesSubsequentPages()
        {
            CreateResponses(
                10,
                null,
                CreateFeedResponse(0, 0, "1"),
                CreateFeedResponse(0, 1, "2"),
                CreateFeedResponse(0, 0, "3"),
                CreateFeedResponse(1, 1, null));

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 },
                mustNotExceedMaxItemCount: false);

            Assert.Equal(Enumerable.Range(0, 2), results);
            Assert.Null(continuationToken);
        }

        [Fact]
        public async Task GivenAQueryWhereItemCountMustNotBeExceeded_WhenExecuted_FetchesSubsequentPagesByIssuingNewQueries()
        {
            CreateResponses(
                10,
                null,
                CreateFeedResponse(0, 0, "1"),
                CreateFeedResponse(0, 1, "2"),
                CreateFeedResponse(10, 10, "3")); // if this shows up in the results, it means we did not issue a new query after the previous page yielded a result

            CreateResponses(
                9,
                "2",
                CreateFeedResponse(0, 0, "3"),
                CreateFeedResponse(1, 1, null));

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 },
                mustNotExceedMaxItemCount: true);

            Assert.Equal(Enumerable.Range(0, 2), results);
            Assert.Null(continuationToken);
        }

        [Fact]
        public async Task GivenAQuery_WithPagesWithFewResults_GivesUpAfterHalfTheResultsHaveBeenCollected()
        {
            CreateResponses(
                10,
                null,
                CreateFeedResponse(0, 1, "1"),
                CreateFeedResponse(1, 1, "2"),
                CreateFeedResponse(2, 1, "3"),
                CreateFeedResponse(3, 1, "4"),
                CreateFeedResponse(4, 1, "5"),
                CreateFeedResponse(5, 1, "6"),
                CreateFeedResponse(6, 1, "7"),
                CreateFeedResponse(7, 1, "8"),
                CreateFeedResponse(8, 1, "9"),
                CreateFeedResponse(9, 1, null));

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 },
                mustNotExceedMaxItemCount: false);

            Assert.Equal(Enumerable.Range(0, 5), results);
            Assert.Equal("5", continuationToken);
        }

        [Fact]
        public async Task GivenAnUpsertDuringABatch_When503ExceptionOccurs_RetryWillHappen()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "id1";
            observation.VersionId = "version1";
            observation.Meta.Profile = new List<string> { "test" };
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            var searchIndex = new SearchIndexEntry(new SearchParameterInfo("newSearchParam1", "newSearchParam1"), new NumberSearchValue(1));
            var searchIndex2 = new SearchIndexEntry(new SearchParameterInfo("newSearchParam2", "newSearchParam2"), new StringSearchValue("paramValue"));

            wrapper.SearchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };
            var innerException = new Exception("RequestTimeout");

            _container.Value.When(x => x.CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())).
                Do(x => throw CreateCosmosException(innerException, HttpStatusCode.ServiceUnavailable));

            // using try catch here instead of Assert.ThrowsAsync in order to verify exception property
            try
            {
                await _dataStore.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
            }
            catch (CosmosException e)
            {
                Assert.Equal(HttpStatusCode.RequestTimeout, e.StatusCode);
            }

            await _container.Value.ReceivedWithAnyArgs(7).CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnUpsertDuringABatch_When408ExceptionOccurs_RetryWillHappen()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "id1";
            observation.VersionId = "version1";
            observation.Meta.Profile = new List<string> { "test" };
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            var searchIndex = new SearchIndexEntry(new SearchParameterInfo("newSearchParam1", "newSearchParam1"), new NumberSearchValue(1));
            var searchIndex2 = new SearchIndexEntry(new SearchParameterInfo("newSearchParam2", "newSearchParam2"), new StringSearchValue("paramValue"));

            wrapper.SearchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };
            var innerException = new Exception("RequestTimeout");

            _container.Value.When(x => x.CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())).
                Do(x => throw CreateCosmosException(innerException, HttpStatusCode.RequestTimeout));

            try
            {
                await _dataStore.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
            }
            catch (CosmosException e)
            {
                Assert.Equal(HttpStatusCode.RequestTimeout, e.StatusCode);
            }

            await _container.Value.ReceivedWithAnyArgs(7).CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
        }

        private void CreateResponses(int pageSize, string continuationToken, params FeedResponse<int>[] responses)
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(
                    Arg.Any<Container>(),
                    Arg.Is<CosmosQueryContext>(ctx =>
                        ctx.FeedOptions.MaxItemCount == pageSize && ctx.ContinuationToken == continuationToken))
                .Returns(cosmosQuery);

            int yieldedIndex = -1;
            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(ci => responses[++yieldedIndex]);
            cosmosQuery.HasMoreResults.Returns(ci => responses[yieldedIndex].ContinuationToken != null);
        }

        private static FeedResponse<int> CreateFeedResponse(int start, int count, string continuationToken)
        {
            FeedResponse<int> feedResponse = Substitute.For<FeedResponse<int>>();
            feedResponse.Count.Returns(count);
            feedResponse.GetEnumerator().Returns(Enumerable.Range(start, count).GetEnumerator());
            feedResponse.ContinuationToken.Returns(continuationToken);
            return feedResponse;
        }

        private CosmosException CreateCosmosException(Exception innerException, HttpStatusCode? statusCode = null)
        {
            CosmosException cosmosException = null;
            if (statusCode.HasValue)
            {
                cosmosException = new CosmosException("message", statusCode.Value, 0, "id", 0.0);
            }
            else
            {
                cosmosException = (CosmosException)RuntimeHelpers.GetUninitializedObject(typeof(CosmosException));
            }

            var sampleException = new Exception(null, innerException);
            foreach (FieldInfo fieldInfo in typeof(Exception).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (typeof(Exception).IsAssignableFrom(fieldInfo.FieldType) &&
                    fieldInfo.GetValue(sampleException) == innerException)
                {
                    fieldInfo.SetValue(cosmosException, innerException);
                }
            }

            return cosmosException;
        }
    }
}
