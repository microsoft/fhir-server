// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Health;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ClearExtensions;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Health
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosHealthCheckTests
    {
        private readonly Container _container = Substitute.For<Container>();
        private readonly ICosmosClientTestProvider _testProvider = Substitute.For<ICosmosClientTestProvider>();
        private readonly CosmosDataStoreConfiguration _configuration = new CosmosDataStoreConfiguration { DatabaseId = "mydb" };
        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration = new CosmosCollectionConfiguration { CollectionId = "mycoll" };

        private readonly TestCosmosHealthCheck _healthCheck;

        public CosmosHealthCheckTests()
        {
            var optionsSnapshot = Substitute.For<IOptionsSnapshot<CosmosCollectionConfiguration>>();
            optionsSnapshot.Get(Microsoft.Health.Fhir.CosmosDb.Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            _healthCheck = new TestCosmosHealthCheck(
                new NonDisposingScope(_container),
                _configuration,
                optionsSnapshot,
                _testProvider,
                NullLogger<TestCosmosHealthCheck>.Instance);
        }

        [Fact]
        public async Task GivenCosmosDbCanBeQueried_WhenHealthIsChecked_ThenHealthyStateShouldBeReturned()
        {
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }

        [Theory]
        [InlineData(typeof(CosmosOperationCanceledException))]
        [InlineData(typeof(CosmosException))]
        public async Task GivenCosmosDb_WhenRetryableExceptionIsAlwaysThrown_ThenUnhealthyStateShouldBeReturned(Type exceptionType)
        {
            // Arrange
            Exception exception;

            if (exceptionType == typeof(CosmosOperationCanceledException))
            {
                exception = new CosmosOperationCanceledException(
                    originalException: new OperationCanceledException(),
                    diagnostics: Substitute.For<CosmosDiagnostics>());
            }
            else if (exceptionType == typeof(CosmosException))
            {
                exception = new CosmosException(
                    message: "Service Unavailable",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: 0,
                    activityId: Guid.NewGuid().ToString(),
                    requestCharge: 0);
            }
            else
            {
                throw new ArgumentException("Unsupported exception type.");
            }

            _testProvider.PerformTestAsync(default, CancellationToken.None).ThrowsForAnyArgs(exception);

            // Act
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            _testProvider.ReceivedWithAnyArgs(3); // Ensure the maximum retries were attempted
        }

        [Theory]
        [InlineData(typeof(CosmosOperationCanceledException))]
        [InlineData(typeof(CosmosException))]
        public async Task GivenCosmosDb_WhenRetryableExceptionIsOnceThrown_ThenHealthyStateShouldBeReturned(Type exceptionType)
        {
            // Arrange
            Exception exception;

            if (exceptionType == typeof(CosmosOperationCanceledException))
            {
                exception = new CosmosOperationCanceledException(
                    originalException: new OperationCanceledException(),
                    diagnostics: Substitute.For<CosmosDiagnostics>());
            }
            else if (exceptionType == typeof(CosmosException))
            {
                exception = new CosmosException(
                    message: "Service Unavailable",
                    statusCode: System.Net.HttpStatusCode.ServiceUnavailable,
                    subStatusCode: 0,
                    activityId: Guid.NewGuid().ToString(),
                    requestCharge: 0);
            }
            else
            {
                throw new ArgumentException("Unsupported exception type.");
            }

            int runs = 0;

            // Simulate failure on the first attempt and success on subsequent attempts
            _testProvider.PerformTestAsync(default, CancellationToken.None)
                .ReturnsForAnyArgs(_ =>
                {
                    runs++;
                    if (runs == 1)
                    {
                        throw exception;
                    }

                    return Task.CompletedTask;
                });

            // Act
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            // Assert
            Assert.Equal(HealthStatus.Healthy, result.Status); // Final state should be Healthy
            Assert.Equal(2, runs); // Ensure 2 attempts were made
            _testProvider.ReceivedWithAnyArgs(2); // Verify PerformTestAsync was called twice
        }

        [Fact]
        public async Task GivenCosmosDbCannotBeQueried_WhenHealthIsChecked_ThenUnhealthyStateShouldBeReturned()
        {
            _testProvider.PerformTestAsync(default, CancellationToken.None).ThrowsForAnyArgs<HttpRequestException>();
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }

        [Fact]
        public async Task GivenCosmosAccessIsForbidden_IsClientCmkError_WhenHealthIsChecked_ThenHealthyStateShouldBeReturned()
        {
            foreach (int clientCmkIssue in new List<int>(Enum.GetValues(typeof(KnownCosmosDbCmkSubStatusValueClientIssue)).Cast<int>()))
            {
                var cosmosException = new CosmosException("Some error message", HttpStatusCode.Forbidden, subStatusCode: clientCmkIssue, activityId: null, requestCharge: 0);
                _testProvider.ClearSubstitute();
                _testProvider.PerformTestAsync(default, CancellationToken.None).ThrowsForAnyArgs(cosmosException);

                HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

                Assert.Equal(HealthStatus.Degraded, result.Status);

                Assert.NotNull(result.Data);
                Assert.True(result.Data.Any());

                VerifyErrorInResult(result.Data, "Reason", HealthStatusReason.CustomerManagedKeyAccessLost.ToString());

                VerifyErrorInResult(result.Data, "Error", FhirHealthErrorCode.Error412.ToString());
            }
        }

        [Fact]
        public async Task GivenCosmosAccessIsForbidden_IsNotClientCmkError_WhenHealthIsChecked_ThenUnhealthyStateShouldBeReturned()
        {
            const int SomeNonClientErrorSubstatusCode = 12345;
            _testProvider.PerformTestAsync(default, CancellationToken.None)
                .ThrowsForAnyArgs(new CosmosException(
                    "An error message",
                    HttpStatusCode.Forbidden,
                    subStatusCode: SomeNonClientErrorSubstatusCode,
                    activityId: null,
                    requestCharge: 0));

            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);

            Assert.NotNull(result.Data);
            Assert.True(result.Data.Any());
            VerifyErrorInResult(result.Data, "Error", FhirHealthErrorCode.Error500.ToString());
        }

        [Fact]
        public async Task GivenCosmosDbWithTooManyRequests_WhenHealthIsChecked_ThenHealthyStateShouldBeReturned()
        {
            _testProvider.PerformTestAsync(default, CancellationToken.None)
                .ThrowsForAnyArgs(new Exception(null, new RequestRateExceededException(TimeSpan.Zero)));

            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            VerifyErrorInResult(result.Data, "Error", FhirHealthErrorCode.Error429.ToString());
        }

        [Fact]
        public async Task GivenCosmosDbWithTimeout_WhenHealthIsChecked_ThenHealthyStateShouldBeReturned()
        {
            var exception = new CosmosException(
                    message: "RequestTimeout",
                    statusCode: HttpStatusCode.RequestTimeout,
                    subStatusCode: 0,
                    activityId: Guid.NewGuid().ToString(),
                    requestCharge: 0);

            _testProvider.PerformTestAsync(default, CancellationToken.None)
                .ThrowsForAnyArgs(exception);

            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Degraded, result.Status);
            VerifyErrorInResult(result.Data, "Error", FhirHealthErrorCode.Error408.ToString());
        }

        private void VerifyErrorInResult(IReadOnlyDictionary<string, object> dictionary, string key, string expectedMessage)
        {
            if (dictionary.TryGetValue(key, out var actualValue))
            {
                Assert.Equal(expectedMessage, actualValue.ToString());
            }
            else
            {
                Assert.Fail($"Expected key '{key}' not found in the dictionary.");
            }
        }
    }
}
