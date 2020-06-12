// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Queries;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class FhirDocumentClientInitializerTests
    {
        private readonly FhirCosmosClientInitializer _documentClientInitializer;

        private readonly Container _documentClient = Substitute.ForPartsOf<Container>();
        private readonly ICollectionInitializer _collectionInitializer1 = Substitute.For<ICollectionInitializer>();
        private readonly ICollectionInitializer _collectionInitializer2 = Substitute.For<ICollectionInitializer>();
        private readonly List<ICollectionInitializer> _collectionInitializers;
        private CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;

        public FhirDocumentClientInitializerTests()
        {
            var documentClientTestProvider = Substitute.For<IDocumentClientTestProvider>();
            var fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
            var cosmosResponseProcessor = Substitute.For<ICosmosResponseProcessor>();
            _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();

            _documentClientInitializer = new FhirCosmosClientInitializer(
                documentClientTestProvider,
                fhirRequestContextAccessor,
                cosmosResponseProcessor,
                Enumerable.Empty<RequestHandler>(),
                NullLogger<FhirCosmosClientInitializer>.Instance);

            _collectionInitializers = new List<ICollectionInitializer> { _collectionInitializer1, _collectionInitializer2 };
        }

        [Fact]
        public void CreateDocumentClient_NullPreferredLocations_DoesNotSetPreferredLocations()
        {
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.Null(documentClient.ClientOptions.ApplicationPreferredRegions);
        }

        [Fact]
        public void CreateDocumentClient_EmptyPreferredLocations_DoesNotSetPreferredLocations()
        {
            _cosmosDataStoreConfiguration.PreferredLocations = new string[] { };
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.Null(documentClient.ClientOptions.ApplicationPreferredRegions);
        }

        [Fact]
        public void CreateDocumentClient_SetsPreferredLocations()
        {
            _cosmosDataStoreConfiguration.PreferredLocations = new[] { "southcentralus", "northcentralus" };
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.NotEmpty(documentClient.ClientOptions.ApplicationPreferredRegions);
            Assert.Equal(2, documentClient.ClientOptions.ApplicationPreferredRegions.Count);

            for (int i = 0; i < _cosmosDataStoreConfiguration.PreferredLocations.Count; i++)
            {
                Assert.Equal(_cosmosDataStoreConfiguration.PreferredLocations[i], documentClient.ClientOptions.ApplicationPreferredRegions[i]);
            }
        }

        [Fact]
        public void CreateDocumentClient_SetsMaxRetryAttemptsOnThrottledRequests()
        {
            _cosmosDataStoreConfiguration.RetryOptions.MaxNumberOfRetries = 10;
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.Equal(10, documentClient.ClientOptions.MaxRetryAttemptsOnRateLimitedRequests);
        }

        [Fact]
        public void CreateDocumentClient_SetsMaxRetryWaitTimeInSeconds()
        {
            _cosmosDataStoreConfiguration.RetryOptions.MaxWaitTimeInSeconds = 99;
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.Equal(TimeSpan.FromSeconds(99), documentClient.ClientOptions.MaxRetryWaitTimeOnRateLimitedRequests);
        }
    }
}
