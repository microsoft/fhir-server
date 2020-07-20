// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Health
{
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

        [Fact]
        public async Task GivenCosmosDbCannotBeQueried_WhenHealthIsChecked_ThenUnhealthyStateShouldBeReturned()
        {
            _testProvider.PerformTest(default, default, _cosmosCollectionConfiguration).ThrowsForAnyArgs<HttpRequestException>();
            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
        }

        [Fact]
        public async Task GivenCosmosDbWithTooManyRequests_WhenHealthIsChecked_ThenHealthyStateShouldBeReturned()
        {
            _testProvider.PerformTest(default, default, _cosmosCollectionConfiguration)
                .ThrowsForAnyArgs(new RequestRateExceededException(TimeSpan.Zero));

            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("rate limit", result.Description);
        }
    }
}
