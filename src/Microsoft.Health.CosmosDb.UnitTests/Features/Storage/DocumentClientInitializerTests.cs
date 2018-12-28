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
using NSubstitute;
using Xunit;

namespace Microsoft.Health.CosmosDb.UnitTests.Features.Storage
{
    public class DocumentClientInitializerTests
    {
        private readonly DocumentClientInitializer _documentClientInitializer;
        private readonly IDocumentClient _documentClient = Substitute.For<IDocumentClient>();
        private readonly ICollectionInitializer _collectionInitializer1 = Substitute.For<ICollectionInitializer>();
        private readonly ICollectionInitializer _collectionInitializer2 = Substitute.For<ICollectionInitializer>();
        private readonly List<ICollectionInitializer> _collectionInitializers;
        private readonly CosmosDataStoreConfiguration cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
        {
            AllowDatabaseCreation = false,
            ConnectionMode = ConnectionMode.Direct,
            ConnectionProtocol = Protocol.Https,
            DatabaseId = "testdatabaseid",
            Host = "https://fakehost",
            Key = "ZmFrZWtleQ==",   // "fakekey"
            PreferredLocations = null,
        };

        public DocumentClientInitializerTests()
        {
            var documentClientTestProvider = Substitute.For<IDocumentClientTestProvider>();

            _documentClientInitializer = new DocumentClientInitializer(documentClientTestProvider, NullLogger<DocumentClientInitializer>.Instance);

            _collectionInitializers = new List<ICollectionInitializer> { _collectionInitializer1, _collectionInitializer2 };
            _documentClient.CreateDatabaseIfNotExistsAsync(Arg.Any<Database>(), Arg.Any<RequestOptions>()).Returns(Substitute.For<ResourceResponse<Database>>());
        }

        [Fact]
        public async void GivenMultipleCollections_WhenInitializing_ThenEachCollectionInitializeMethodIsCalled()
        {
            await _documentClientInitializer.InitializeDataStore(_documentClient, cosmosDataStoreConfiguration, _collectionInitializers);

            await _collectionInitializer1.Received(1).InitializeCollection(_documentClient);
            await _collectionInitializer2.Received(1).InitializeCollection(_documentClient);
        }

        [Fact]
        public async void GivenAConfigurationWithNoDatabaseCreation_WhenInitializing_ThenCreateDatabaseIfNotExistsIsNotCalled()
        {
            await _documentClientInitializer.InitializeDataStore(_documentClient, cosmosDataStoreConfiguration, _collectionInitializers);

            await _documentClient.DidNotReceive().CreateDatabaseIfNotExistsAsync(Arg.Any<Database>(), Arg.Any<RequestOptions>());
        }

        [Fact]
        public async void GivenAConfigurationWithDatabaseCreation_WhenInitializing_ThenCreateDatabaseIfNotExistsIsCalled()
        {
            cosmosDataStoreConfiguration.AllowDatabaseCreation = true;

            await _documentClientInitializer.InitializeDataStore(_documentClient, cosmosDataStoreConfiguration, _collectionInitializers);

            await _documentClient.Received(1).CreateDatabaseIfNotExistsAsync(Arg.Any<Database>(), Arg.Any<RequestOptions>());
        }
    }
}
