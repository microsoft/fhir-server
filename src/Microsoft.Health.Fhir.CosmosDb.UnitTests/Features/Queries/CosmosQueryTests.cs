// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Queries
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosQueryTests
    {
        private readonly ICosmosQueryContext _queryContext;
        private readonly FeedIterator<object> _feedIterator;
        private readonly ICosmosResponseProcessor _processor;
        private readonly ICosmosQueryLogger _logger;

        public CosmosQueryTests()
        {
            _queryContext = Substitute.For<ICosmosQueryContext>();
            _feedIterator = Substitute.For<FeedIterator<object>>();
            _processor = Substitute.For<ICosmosResponseProcessor>();
            _logger = Substitute.For<ICosmosQueryLogger>();
        }

        [Fact]
        public async Task GivenAMalformedContinuationTokenException_WhenExecutingQuery_ThenRequestNotValidExceptionIsThrown()
        {
            // Arrange - simulate the internal SDK exception type
            var malformedException = new MalformedContinuationTokenTestException(
                "ParallelContinuationToken is missing field: 'token': {\"compositeToken\":{\"token\":null}}");

            _feedIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Throws(malformedException);

            var cosmosQuery = new CosmosQuery<object>(_queryContext, _feedIterator, _processor, _logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(
                () => cosmosQuery.ExecuteNextAsync(CancellationToken.None));

            Assert.Equal(Microsoft.Health.Fhir.Core.Resources.InvalidContinuationToken, exception.Message);
        }

        [Fact]
        public async Task GivenACosmosException_WhenExecutingQuery_ThenProcessErrorResponseIsCalled()
        {
            // Arrange
            var cosmosException = new TestCosmosException("test error", System.Net.HttpStatusCode.TooManyRequests);

            _feedIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Throws(cosmosException);

            var cosmosQuery = new CosmosQuery<object>(_queryContext, _feedIterator, _processor, _logger);

            // Act & Assert - CosmosException should still propagate after processing
            await Assert.ThrowsAsync<TestCosmosException>(
                () => cosmosQuery.ExecuteNextAsync(CancellationToken.None));

            await _processor.Received(1).ProcessErrorResponseAsync(
                Arg.Any<System.Net.HttpStatusCode>(),
                Arg.Any<Headers>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAGenericException_WhenExecutingQuery_ThenExceptionIsNotCaught()
        {
            // Arrange - a non-MalformedContinuationToken exception should not be caught
            var genericException = new InvalidOperationException("some other error");

            _feedIterator.ReadNextAsync(Arg.Any<CancellationToken>()).Throws(genericException);

            var cosmosQuery = new CosmosQuery<object>(_queryContext, _feedIterator, _processor, _logger);

            // Act & Assert - generic exceptions should propagate unchanged
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => cosmosQuery.ExecuteNextAsync(CancellationToken.None));
        }

        /// <summary>
        /// Test exception that mimics the internal Cosmos DB SDK MalformedContinuationTokenException.
        /// The real exception is internal to the SDK, so we use a test double with a matching type name.
        /// </summary>
        private sealed class MalformedContinuationTokenTestException : Exception
        {
            public MalformedContinuationTokenTestException(string message)
                : base(message)
            {
            }
        }

        /// <summary>
        /// Test CosmosException for unit testing.
        /// </summary>
        private sealed class TestCosmosException : CosmosException
        {
            public TestCosmosException(string message, System.Net.HttpStatusCode statusCode)
                : base(message, statusCode, 0, string.Empty, 0)
            {
            }
        }
    }
}
