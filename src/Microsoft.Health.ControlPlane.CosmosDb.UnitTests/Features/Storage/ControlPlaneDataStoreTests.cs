// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.ControlPlane.CosmosDb.UnitTests.Features.Storage
{
    public class ControlPlaneDataStoreTests
    {
        private readonly ControlPlaneDataStore _controlPlaneDataStore;
        private readonly CosmosIdentityProvider _cosmosIdentityProvider = Substitute.For<CosmosIdentityProvider>();
        private readonly ICosmosDocumentQueryFactory _cosmosDocumentQueryFactory = Substitute.For<ICosmosDocumentQueryFactory>();
        private readonly IDocumentClient _documentClient = Substitute.For<IDocumentClient>();

        public ControlPlaneDataStoreTests()
        {
            var scopedIDocumentClient = Substitute.For<IScoped<IDocumentClient>>();
            scopedIDocumentClient.Value.Returns(_documentClient);

            var cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
            {
                AllowDatabaseCreation = false,
                ConnectionMode = ConnectionMode.Direct,
                ConnectionProtocol = Protocol.Https,
                DatabaseId = "testdatabaseid",
                Host = "https://fakehost",
                Key = "ZmFrZWtleQ==",   // "fakekey"
                PreferredLocations = null,
            };

            _cosmosIdentityProvider.ETag.Returns("\"1\"");
            _cosmosIdentityProvider.Name.Returns("aad");
            _cosmosIdentityProvider.Audience.Returns(new[] { "fhir-api" });
            _cosmosIdentityProvider.Authority.Returns("https://login.microsoftonline.com/common");
            SetupDocumentQuery<CosmosIdentityProvider>(_cosmosIdentityProvider);

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();
            optionsMonitor.Get(Constants.CollectionConfigurationName).Returns(new CosmosCollectionConfiguration { CollectionId = "collectionId" });

            var logger = NullLogger<ControlPlaneDataStore>.Instance;

            _controlPlaneDataStore = new ControlPlaneDataStore(
                scopedIDocumentClient,
                cosmosDataStoreConfiguration,
                _cosmosDocumentQueryFactory,
                optionsMonitor,
                logger);
        }

        private void SetupDocumentQuery<T>(object responseObject)
        {
            var documentQuery = Substitute.For<IDocumentQuery<T>>();

            var feedResponse = new FeedResponse<dynamic>(new List<dynamic> { responseObject });
            documentQuery.ExecuteNextAsync(Arg.Any<CancellationToken>()).Returns(feedResponse);
            _cosmosDocumentQueryFactory.Create<T>(Arg.Any<IDocumentClient>(), Arg.Any<CosmosQueryContext>()).Returns(documentQuery);
        }

        [Fact]
        public async void GivenAName_WhenGettingIdentityProvider_ThenIdentityProviderReturned()
        {
            var identityProvider = await _controlPlaneDataStore.GetIdentityProviderAsync("aad", CancellationToken.None);

            Assert.Equal(_cosmosIdentityProvider.Name, identityProvider.Name);
        }

        [Fact]
        public async void GivenABootstrapHash_WhenCallingIsBootstrappedWithPreviousBootstrapped_ThenTrueIsReturned()
        {
            var cosmosBootstrap = Substitute.For<CosmosBootstrap>();
            SetupDocumentQuery<CosmosBootstrap>(cosmosBootstrap);

            var bootstrapHash = "hash";

            var response = await _controlPlaneDataStore.IsBootstrappedAsync(bootstrapHash, CancellationToken.None);

            Assert.True(response);
        }
    }
}
