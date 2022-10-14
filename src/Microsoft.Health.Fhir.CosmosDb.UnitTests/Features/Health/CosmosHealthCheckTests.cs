// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp.Common;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.CosmosDb.Configs;
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

        [Fact]
        public async Task GivenCosmosDbCannotBeQueried_WhenHealthIsChecked_ThenUnhealthyStateShouldBeReturned()
        {
            _testProvider.PerformTest(default, default, _cosmosCollectionConfiguration).ThrowsForAnyArgs<HttpRequestException>();
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
                _testProvider.PerformTest(default, default, _cosmosCollectionConfiguration).ThrowsForAnyArgs(cosmosException);

                HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

                Assert.Equal(HealthStatus.Unhealthy, result.Status);
                Assert.Contains("customer-managed key is not available", result.Description);
                Assert.NotNull(result.Data);
                Assert.True(result.Data.ContainsKey("IsCustomerManagedKeyError"));
                Assert.True((bool)result.Data["IsCustomerManagedKeyError"]);
                Assert.NotNull(result.Exception);
                Assert.Equal(cosmosException, result.Exception);
            }
        }

        [Fact]
        public async Task GivenCosmosAccessIsForbidden_IsNotClientCmkError_WhenHealthIsChecked_ThenUnhealthyStateShouldBeReturned()
        {
            const int SomeNonClientErrorSubstatusCode = 12345;
            _testProvider.PerformTest(default, default, _cosmosCollectionConfiguration)
                .ThrowsForAnyArgs(new CosmosException(
                    "An error message",
                    HttpStatusCode.Forbidden,
                    subStatusCode: SomeNonClientErrorSubstatusCode,
                    activityId: null,
                    requestCharge: 0));

            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.DoesNotContain("customer-managed key is not available", result.Description);
            Assert.False(result.Data.Any());
            Assert.Null(result.Exception);
        }

        [Fact]
        public async Task GivenCosmosDbWithTooManyRequests_WhenHealthIsChecked_ThenHealthyStateShouldBeReturned()
        {
            _testProvider.PerformTest(default, default, _cosmosCollectionConfiguration)
                .ThrowsForAnyArgs(new Exception(null, new RequestRateExceededException(TimeSpan.Zero)));

            HealthCheckResult result = await _healthCheck.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
            Assert.Contains("rate limit", result.Description);
        }
    }
}
