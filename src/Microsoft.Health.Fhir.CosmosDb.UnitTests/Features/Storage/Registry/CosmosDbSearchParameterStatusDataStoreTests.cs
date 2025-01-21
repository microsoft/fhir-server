// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Polly;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Registry;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Search)]
public class CosmosDbSearchParameterStatusDataStoreTests
{
    private readonly CosmosDbSearchParameterStatusDataStore _dataStore;
    private readonly ICosmosQueryFactory _cosmosQueryFactory;
    private readonly IScoped<Container> _containerScope;
    private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();
    private readonly IFhirRequestContext _fhirRequestContext;
    private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
    private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;

    public CosmosDbSearchParameterStatusDataStoreTests()
    {
        _cosmosQueryFactory = Substitute.For<ICosmosQueryFactory>();
        _containerScope = Substitute.For<IScoped<Container>>();

        _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        _requestContextAccessor.RequestContext.Returns(_fhirRequestContext);
        _retryExceptionPolicyFactory = new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, _requestContextAccessor, NullLogger<RetryExceptionPolicyFactory>.Instance);

        _dataStore = new CosmosDbSearchParameterStatusDataStore(
            () => _containerScope,
            new CosmosDataStoreConfiguration(),
            _cosmosQueryFactory,
            _retryExceptionPolicyFactory);
    }

    [Fact]
    public async Task GivenTransientError_WhenGettingSearchParameterStatusesAsBackgroundTask_ThenRetriesShouldBeAttempted()
    {
        // Arrange
        _fhirRequestContext.IsBackgroundTask.Returns(true);

        var exception = new CosmosException(
                    message: "Service Unavailable",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: 0,
                    activityId: Guid.NewGuid().ToString(),
                    requestCharge: 0);

        var mockQuery = Substitute.For<ICosmosQuery<SearchParameterStatusWrapper>>();
        int runs = 0;

        // Simulate failure on the first three attempts and success on fourth
        mockQuery.ExecuteNextAsync(CancellationToken.None)
            .ReturnsForAnyArgs(_ =>
            {
                runs++;
                if (runs < 4)
                {
                    throw exception;
                }

                // Return a mock FeedResponse on the fourth attempt
                return Task.FromResult(CreateMockFeedResponse(
                [
                    new SearchParameterStatusWrapper { Uri = new Uri("http://example.com") },
                ]));
            });

        _cosmosQueryFactory.Create<SearchParameterStatusWrapper>(
            Arg.Any<Container>(),
            Arg.Any<CosmosQueryContext>()).Returns(mockQuery);

        // Act
        await _dataStore.GetSearchParameterStatuses(CancellationToken.None);

        // Assert
        await mockQuery.Received(4).ExecuteNextAsync(Arg.Any<CancellationToken>()); // 1 initial attempt + 3 retry
    }

    [Fact]
    public async Task GivenTransientError_WhenCheckingIfSearchParameterStatusUpdateIsRequiredAsBackgroundTask_ThenRetriesShouldBeAttempted()
    {
        // Arrange
        _fhirRequestContext.IsBackgroundTask.Returns(true);

        var exception = new CosmosException(
            message: "Service Unavailable",
            statusCode: HttpStatusCode.ServiceUnavailable,
            subStatusCode: 0,
            activityId: Guid.NewGuid().ToString(),
            requestCharge: 0);

        var mockQuery = Substitute.For<ICosmosQuery<CosmosDbSearchParameterStatusDataStore.CacheQueryResponse>>();
        int runs = 0;

        // Simulate failure on the first three attempts and success on the fourth
        mockQuery.ExecuteNextAsync(Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(_ =>
            {
                runs++;
                if (runs < 4)
                {
                    throw exception;
                }

                // Return a mock FeedResponse on the fourth attempt
                return Task.FromResult(CreateMockFeedResponse(new List<CosmosDbSearchParameterStatusDataStore.CacheQueryResponse>
                {
                    new()
                    {
                        Count = 5,
                        LastUpdated = DateTimeOffset.UtcNow.AddMinutes(1),
                    },
                }));
            });

        _cosmosQueryFactory.Create<CosmosDbSearchParameterStatusDataStore.CacheQueryResponse>(
            Arg.Any<Container>(),
            Arg.Any<CosmosQueryContext>()).Returns(mockQuery);

        var container = Substitute.For<IScoped<Container>>();
        container.Value.Returns(Substitute.For<Container>());

        // Act
        var result = await _dataStore.CheckIfSearchParameterStatusUpdateRequiredAsync(
            container,
            currentCount: 4,
            lastRefreshed: DateTimeOffset.UtcNow,
            CancellationToken.None);

        // Assert
        Assert.True(result); // Should return true due to mismatched count and timestamp
        await mockQuery.Received(4).ExecuteNextAsync(Arg.Any<CancellationToken>()); // 1 initial attempt + 3 retries
    }

    [Fact]
    public async Task GivenBatchExecutionError_WhenUpsertingStatusesAsBackgroundTask_ThenRetriesShouldBeAttempted()
    {
        // Arrange
        _fhirRequestContext.IsBackgroundTask.Returns(true);

        var exception = new CosmosException(
            message: "Service Unavailable",
            statusCode: HttpStatusCode.ServiceUnavailable,
            subStatusCode: 0,
            activityId: Guid.NewGuid().ToString(),
            requestCharge: 0);

        var mockContainer = Substitute.For<Container>();
        var mockTransactionalBatch = Substitute.For<TransactionalBatch>();

        int runs = 0;

        // Simulate failure on the first three attempts and success on the fourth
        mockTransactionalBatch.ExecuteAsync(Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(_ =>
            {
                runs++;
                if (runs < 4)
                {
                    throw exception;
                }

                // Return a mock TransactionalBatchResponse on the fourth attempt
                return Task.FromResult(Substitute.For<TransactionalBatchResponse>());
            });

        mockContainer.CreateTransactionalBatch(Arg.Any<PartitionKey>())
            .Returns(mockTransactionalBatch);

        _containerScope.Value.Returns(mockContainer);

        var statuses = new List<ResourceSearchParameterStatus>
        {
            new() { Uri = new Uri("http://example.com") },
        };

        // Act
        await _dataStore.UpsertStatuses(statuses, CancellationToken.None);

        // Assert
        await mockTransactionalBatch.Received(4).ExecuteAsync(Arg.Any<CancellationToken>()); // 3 failure + 1 success
    }

    private static FeedResponse<T> CreateMockFeedResponse<T>(List<T> items)
    {
        var feedResponse = Substitute.For<FeedResponse<T>>();
        feedResponse.Count.Returns(items.Count);
        feedResponse.GetEnumerator().Returns(items.GetEnumerator());
        return feedResponse;
    }
}
