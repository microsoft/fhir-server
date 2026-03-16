// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Azure;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.IntegrationDataStore;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Polly;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.IntegrationDataStore
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class IntegrationStoreRetryExceptionPolicyFactoryTests
    {
        [Fact]
        public void GivenNullConfiguration_WhenCreatingFactory_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => new IntegrationStoreRetryExceptionPolicyFactory(null));
        }

        [Fact]
        public void GivenValidConfiguration_WhenCreatingFactory_ThenFactoryIsCreated()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            // Act
            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);

            // Assert
            Assert.NotNull(factory);
            Assert.NotNull(factory.RetryPolicy);
        }

        [Fact]
        public void GivenFactory_WhenAccessingRetryPolicy_ThenReturnsPolicy()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);

            // Act
            var policy = factory.RetryPolicy;

            // Assert
            Assert.NotNull(policy);
        }

        [Fact]
        public async Task GivenSuccessfulOperation_WhenExecutingWithPolicy_ThenCompletesSuccessfully()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act
            await factory.RetryPolicy.ExecuteAsync(() =>
            {
                executionCount++;
                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(1, executionCount);
        }

        [Fact]
        public async Task GivenTransientFailure_WhenExecutingWithPolicy_ThenRetries()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act
            await factory.RetryPolicy.ExecuteAsync(() =>
            {
                executionCount++;
                if (executionCount < 2)
                {
                    throw new RequestFailedException("Transient error");
                }

                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(2, executionCount);
        }

        [Fact]
        public async Task GivenMaxRetryExceeded_WhenExecutingWithPolicy_ThenThrowsException()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 2,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act & Assert
            await Assert.ThrowsAsync<RequestFailedException>(async () =>
            {
                await factory.RetryPolicy.ExecuteAsync(() =>
                {
                    executionCount++;
                    throw new RequestFailedException("Persistent error");
                });
            });

            Assert.Equal(3, executionCount); // Initial + 2 retries
        }

        [Fact]
        public async Task GivenNoTimeout_WhenExecutingWithPolicy_ThenRetriesWithoutTimeoutCheck()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 0,
                MaxWaitTimeInSeconds = -1, // No timeout
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act
            await factory.RetryPolicy.ExecuteAsync(() =>
            {
                executionCount++;
                if (executionCount < 3)
                {
                    throw new RequestFailedException("Transient error");
                }

                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(3, executionCount);
        }

        [Fact]
        public async Task GivenZeroRetryCount_WhenExecutingWithPolicy_ThenDoesNotRetry()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 0,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act & Assert
            await Assert.ThrowsAsync<RequestFailedException>(async () =>
            {
                await factory.RetryPolicy.ExecuteAsync(() =>
                {
                    executionCount++;
                    throw new RequestFailedException("Error");
                });
            });

            Assert.Equal(1, executionCount); // Only initial attempt, no retries
        }

        [Fact]
        public async Task GivenNonRequestFailedException_WhenExecutingWithPolicy_ThenDoesNotRetry()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 1,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await factory.RetryPolicy.ExecuteAsync(() =>
                {
                    executionCount++;
                    throw new InvalidOperationException("Non-retryable error");
                });
            });

            Assert.Equal(1, executionCount); // Should not retry for non-RequestFailedException
        }

        [Fact]
        public async Task GivenMultipleRetries_WhenExecutingWithPolicy_ThenRespectsRetryCount()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 5,
                RetryInternalInSecondes = 0,
                MaxWaitTimeInSeconds = -1,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;

            // Act
            await factory.RetryPolicy.ExecuteAsync(() =>
            {
                executionCount++;
                if (executionCount < 4)
                {
                    throw new RequestFailedException("Transient error");
                }

                return Task.CompletedTask;
            });

            // Assert
            Assert.Equal(4, executionCount);
        }

        [Fact]
        public async Task GivenSuccessAfterRetries_WhenExecutingWithPolicy_ThenReturnsResult()
        {
            // Arrange
            var config = Options.Create(new IntegrationDataStoreConfiguration
            {
                MaxRetryCount = 3,
                RetryInternalInSecondes = 0,
                MaxWaitTimeInSeconds = 10,
            });

            var factory = new IntegrationStoreRetryExceptionPolicyFactory(config);
            int executionCount = 0;
            string expectedResult = "Success";

            // Act
            string result = await factory.RetryPolicy.ExecuteAsync(() =>
            {
                executionCount++;
                if (executionCount < 2)
                {
                    throw new RequestFailedException("Transient error");
                }

                return Task.FromResult(expectedResult);
            });

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(2, executionCount);
        }
    }
}
