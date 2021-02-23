// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Internal;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class CosmosFhirDataStoreTests
    {
        private readonly ICosmosQueryFactory _cosmosQueryFactory;
        private readonly CosmosFhirDataStore _dataStore;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();

        public CosmosFhirDataStoreTests()
        {
            _cosmosQueryFactory = Substitute.For<ICosmosQueryFactory>();
            _dataStore = new CosmosFhirDataStore(
                Substitute.For<IScoped<Container>>(),
                _cosmosDataStoreConfiguration,
                Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>(),
                _cosmosQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, Substitute.For<IFhirRequestContextAccessor>()),
                NullLogger<CosmosFhirDataStore>.Instance,
                Options.Create(new CoreFeatureConfiguration()),
                new Lazy<ISupportedSearchParameterDefinitionManager>(Substitute.For<ISupportedSearchParameterDefinitionManager>()));
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

        private CosmosException CreateCosmosException(Exception innerException)
        {
            var cosmosException = (CosmosException)RuntimeHelpers.GetUninitializedObject(typeof(CosmosException));

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
