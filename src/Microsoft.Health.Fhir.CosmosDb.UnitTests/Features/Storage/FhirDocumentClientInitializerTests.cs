// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    public class FhirDocumentClientInitializerTests
    {
        private readonly FhirDocumentClientInitializer _documentClientInitializer;

        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
        {
            AllowDatabaseCreation = false,
            ConnectionMode = ConnectionMode.Direct,
            ConnectionProtocol = Protocol.Https,
            DatabaseId = "testdatabaseid",
            Host = "https://fakehost",
            Key = "ZmFrZWtleQ==",   // "fakekey"
            PreferredLocations = null,
        };

        private readonly IDocumentClient _documentClient = Substitute.For<IDocumentClient>();
        private readonly ICollectionInitializer _collectionInitializer1 = Substitute.For<ICollectionInitializer>();
        private readonly ICollectionInitializer _collectionInitializer2 = Substitute.For<ICollectionInitializer>();
        private readonly List<ICollectionInitializer> _collectionInitializers;

        public FhirDocumentClientInitializerTests()
        {
            var documentClientTestProvider = Substitute.For<IDocumentClientTestProvider>();
            var fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();

            _documentClientInitializer = new FhirDocumentClientInitializer(documentClientTestProvider, fhirRequestContextAccessor, NullLogger<FhirDocumentClientInitializer>.Instance);

            _collectionInitializers = new List<ICollectionInitializer> { _collectionInitializer1, _collectionInitializer2 };
            _documentClient.CreateDatabaseIfNotExistsAsync(Arg.Any<Database>(), Arg.Any<RequestOptions>()).Returns(Substitute.For<ResourceResponse<Database>>());

            _cosmosDataStoreConfiguration.RetryOptions.MaxNumberOfRetries = 10;
            _cosmosDataStoreConfiguration.RetryOptions.MaxWaitTimeInSeconds = 99;
        }

        [Fact]
        public void CreateDocumentClient_NullPreferredLocations_DoesNotSetPreferredLocations()
        {
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.NotNull(documentClient.ConnectionPolicy);
            Assert.Empty(documentClient.ConnectionPolicy.PreferredLocations);
        }

        [Fact]
        public void CreateDocumentClient_EmptyPreferredLocations_DoesNotSetPreferredLocations()
        {
            _cosmosDataStoreConfiguration.PreferredLocations = new string[] { };
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.NotNull(documentClient.ConnectionPolicy);
            Assert.Empty(documentClient.ConnectionPolicy.PreferredLocations);
        }

        [Fact]
        public void CreateDocumentClient_SetsPreferredLocations()
        {
            _cosmosDataStoreConfiguration.PreferredLocations = new[] { "southcentralus", "northcentralus" };
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.NotNull(documentClient.ConnectionPolicy);
            Assert.NotEmpty(documentClient.ConnectionPolicy.PreferredLocations);
            Assert.Equal(2, documentClient.ConnectionPolicy.PreferredLocations.Count);

            for (int i = 0; i < _cosmosDataStoreConfiguration.PreferredLocations.Count; i++)
            {
                Assert.Equal(_cosmosDataStoreConfiguration.PreferredLocations[i], documentClient.ConnectionPolicy.PreferredLocations[i]);
            }
        }

        [Fact]
        public void CreateDocumentClient_SetsMaxRetryAttemptsOnThrottledRequests()
        {
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.NotNull(documentClient.ConnectionPolicy);
            Assert.NotNull(documentClient.ConnectionPolicy.RetryOptions);
            Assert.Equal(10, documentClient.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests);
        }

        [Fact]
        public void CreateDocumentClient_SetsMaxRetryWaitTimeInSeconds()
        {
            var documentClient = _documentClientInitializer.CreateDocumentClient(_cosmosDataStoreConfiguration);

            Assert.NotNull(documentClient.ConnectionPolicy);
            Assert.NotNull(documentClient.ConnectionPolicy.RetryOptions);
            Assert.Equal(99, documentClient.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds);
        }

        [Fact]
        public async void GivenMultipleCollections_WhenInitializing_ThenEachCollectionInitializeMethodIsCalled()
        {
            await _documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration, _collectionInitializers);

            await _collectionInitializer1.Received(1).InitializeCollection(_documentClient);
            await _collectionInitializer2.Received(1).InitializeCollection(_documentClient);
        }

        [Fact]
        public async void GivenAConfigurationWithNoDatabaseCreation_WhenInitializing_ThenCreateDatabaseIfNotExistsIsNotCalled()
        {
            await _documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration, _collectionInitializers);

            await _documentClient.DidNotReceive().CreateDatabaseIfNotExistsAsync(Arg.Any<Database>(), Arg.Any<RequestOptions>());
        }

        [Fact]
        public async void GivenAConfigurationWithDatabaseCreation_WhenInitializing_ThenCreateDatabaseIfNotExistsIsCalled()
        {
            _cosmosDataStoreConfiguration.AllowDatabaseCreation = true;

            await _documentClientInitializer.InitializeDataStore(_documentClient, _cosmosDataStoreConfiguration, _collectionInitializers);

            await _documentClient.Received(1).CreateDatabaseIfNotExistsAsync(Arg.Any<Database>(), Arg.Any<RequestOptions>());
        }
    }
}
