// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirCosmosClientInitializerTests
    {
        private readonly FhirCosmosClientInitializer _initializer;

        private readonly ICollectionInitializer _collectionInitializer1 = Substitute.For<ICollectionInitializer>();
        private readonly ICollectionInitializer _collectionInitializer2 = Substitute.For<ICollectionInitializer>();
        private readonly List<ICollectionInitializer> _collectionInitializers;
        private CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;

        public FhirCosmosClientInitializerTests()
        {
            var clientTestProvider = Substitute.For<ICosmosClientTestProvider>();
            _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();

            _initializer = new FhirCosmosClientInitializer(
                clientTestProvider,
                () => new[] { new TestRequestHandler() },
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, Substitute.For<RequestContextAccessor<IFhirRequestContext>>()),
                NullLogger<FhirCosmosClientInitializer>.Instance);

            _collectionInitializers = new List<ICollectionInitializer> { _collectionInitializer1, _collectionInitializer2 };
        }

        [Fact]
        public void CreateClient_NullPreferredLocations_DoesNotSetPreferredLocations()
        {
            var client = _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);

            Assert.Null(client.ClientOptions.ApplicationPreferredRegions);
        }

        [Fact]
        public void CreateClient_EmptyPreferredLocations_DoesNotSetPreferredLocations()
        {
            _cosmosDataStoreConfiguration.PreferredLocations = new string[] { };
            var client = _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);

            Assert.Null(client.ClientOptions.ApplicationPreferredRegions);
        }

        [Fact]
        public void CreateClient_SetsPreferredLocations()
        {
            _cosmosDataStoreConfiguration.PreferredLocations = new[] { "southcentralus", "northcentralus" };
            var client = _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);

            Assert.NotEmpty(client.ClientOptions.ApplicationPreferredRegions);
            Assert.Equal(2, client.ClientOptions.ApplicationPreferredRegions.Count);

            for (int i = 0; i < _cosmosDataStoreConfiguration.PreferredLocations.Count; i++)
            {
                Assert.Equal(_cosmosDataStoreConfiguration.PreferredLocations[i], client.ClientOptions.ApplicationPreferredRegions[i]);
            }
        }

        [Fact]
        public void CreateClient_SetsMaxRetryAttemptsOnThrottledRequests()
        {
            _cosmosDataStoreConfiguration.RetryOptions.MaxNumberOfRetries = 10;
            var client = _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);

            Assert.Equal(10, client.ClientOptions.MaxRetryAttemptsOnRateLimitedRequests);
        }

        [Fact]
        public void CreateClient_SetsMaxRetryWaitTimeInSeconds()
        {
            _cosmosDataStoreConfiguration.RetryOptions.MaxWaitTimeInSeconds = 99;
            var client = _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);

            Assert.Equal(TimeSpan.FromSeconds(99), client.ClientOptions.MaxRetryWaitTimeOnRateLimitedRequests);
        }

        [Fact]
        public void CreateClient_CreatesNewHandlers()
        {
            // If new handlers are not created the second call will fail
            _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);
            _initializer.CreateCosmosClient(_cosmosDataStoreConfiguration);
        }
    }
}
