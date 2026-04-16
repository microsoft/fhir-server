// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Queues;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.DataSourceValidation)]
public class CosmosQueueClientTests
{
    private readonly ICosmosQueryFactory _cosmosQueryFactory;
    private readonly ICosmosDbDistributedLockFactory _distributedLockFactory;
    private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();
    private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
    private readonly RetryExceptionPolicyFactory _retryPolicyFactory;
    private readonly CosmosQueueClient _cosmosQueueClient;

    public CosmosQueueClientTests()
    {
        _cosmosQueryFactory = Substitute.For<ICosmosQueryFactory>();
        _distributedLockFactory = Substitute.For<ICosmosDbDistributedLockFactory>();
        _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        _retryPolicyFactory = new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, _requestContextAccessor, NullLogger<RetryExceptionPolicyFactory>.Instance);

        _cosmosQueueClient = new CosmosQueueClient(
            Substitute.For<Func<IScoped<Container>>>(),
            _cosmosQueryFactory,
            _distributedLockFactory,
            _retryPolicyFactory,
            NullLogger<CosmosQueueClient>.Instance);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData((HttpStatusCode)449)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task GivenADequeueJobOperation_WhenExceptionOccurs_RetryWillHappen(HttpStatusCode statusCode)
    {
        // Arrange
        ICosmosQuery<JobGroupWrapper> cosmosQuery = Substitute.For<ICosmosQuery<JobGroupWrapper>>();
        _cosmosQueryFactory.Create<JobGroupWrapper>(Arg.Any<Container>(), Arg.Any<CosmosQueryContext>())
            .ReturnsForAnyArgs(cosmosQuery);

        int callCount = 0;
        cosmosQuery.ExecuteNextAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(_ =>
        {
            if (callCount++ == 0)
            {
                throw new TestCosmosException(statusCode);
            }

            return Task.FromResult(Substitute.For<FeedResponse<JobGroupWrapper>>());
        });

        // Act
        await _cosmosQueueClient.DequeueAsync(0, "testworker", 10, CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount);
        await cosmosQuery.ReceivedWithAnyArgs(2).ExecuteNextAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(typeof(CosmosException))]
    [InlineData(typeof(RequestRateExceededException))]
    public async Task GivenADequeueJobOperation_WhenExceptionWithRetryAfterIsProvided_PolicyRespectsRetryAfter(Type exceptionType)
    {
        // Arrange
        ICosmosQuery<JobGroupWrapper> cosmosQuery = Substitute.For<ICosmosQuery<JobGroupWrapper>>();
        _cosmosQueryFactory.Create<JobGroupWrapper>(Arg.Any<Container>(), Arg.Any<CosmosQueryContext>())
            .ReturnsForAnyArgs(cosmosQuery);
        var retryAfter = TimeSpan.FromSeconds(2);
        int callCount = 0;

        cosmosQuery.ExecuteNextAsync(Arg.Any<CancellationToken>()).ReturnsForAnyArgs(_ =>
        {
            if (callCount++ == 0)
            {
                throw exceptionType == typeof(CosmosException)
                    ? new TestCosmosException(HttpStatusCode.TooManyRequests, retryAfter)
                    : new RequestRateExceededException(retryAfter);
            }

            return Task.FromResult(Substitute.For<FeedResponse<JobGroupWrapper>>());
        });

        var stopwatch = Stopwatch.StartNew();

        // Act
        await _cosmosQueueClient.DequeueAsync(0, "testworker", 10, CancellationToken.None);

        stopwatch.Stop();

        // Assert
        Assert.Equal(2, callCount);
        await cosmosQuery.ReceivedWithAnyArgs(2).ExecuteNextAsync(Arg.Any<CancellationToken>());

        // Allowing small imprecision due to timer resolution
        var actualElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        Assert.True(
            Math.Abs(actualElapsedSeconds - retryAfter.TotalSeconds) <= 0.5,
            $"Expected retry after {retryAfter.TotalSeconds} seconds, but actual elapsed time was {actualElapsedSeconds} seconds.");
    }

    public class TestCosmosException : CosmosException
    {
        private readonly TimeSpan? _retryAfter;

        public TestCosmosException(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
            : base("Test exception message", statusCode, 0, "test-activity-id", 0.0)
        {
            _retryAfter = retryAfter;
        }

        public override TimeSpan? RetryAfter => _retryAfter;
    }
}
