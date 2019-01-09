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
        private readonly CosmosIdentityProvider _cosmosIdentityProvider;

        public ControlPlaneDataStoreTests()
        {
            var scopedIDocumentClient = Substitute.For<IScoped<IDocumentClient>>();
            var documentClient = Substitute.For<IDocumentClient>();
            scopedIDocumentClient.Value.Returns(documentClient);

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

            var cosmosDocumentQueryFactory = Substitute.For<ICosmosDocumentQueryFactory>();
            var identityProviderDocumentQuery = Substitute.For<IDocumentQuery<CosmosIdentityProvider>>();

            _cosmosIdentityProvider = Substitute.For<CosmosIdentityProvider>();
            _cosmosIdentityProvider.Name.Returns("aad");
            _cosmosIdentityProvider.Audience.Returns(new[] { "fhir-api" });
            _cosmosIdentityProvider.Authority.Returns("https://login.microsoftonline.com/common");

            var feedResponse = new FeedResponse<dynamic>(new List<dynamic> { _cosmosIdentityProvider });
            identityProviderDocumentQuery.ExecuteNextAsync(Arg.Any<CancellationToken>()).Returns(feedResponse);
            cosmosDocumentQueryFactory.Create<CosmosIdentityProvider>(Arg.Any<IDocumentClient>(), Arg.Any<CosmosQueryContext>()).Returns(identityProviderDocumentQuery);

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();
            optionsMonitor.Get(Constants.CollectionConfigurationName).Returns(new CosmosCollectionConfiguration { CollectionId = "collectionId" });

            var logger = NullLogger<ControlPlaneDataStore>.Instance;

            _controlPlaneDataStore = new ControlPlaneDataStore(
                scopedIDocumentClient,
                cosmosDataStoreConfiguration,
                cosmosDocumentQueryFactory,
                new RetryExceptionPolicyFactory(cosmosDataStoreConfiguration),
                optionsMonitor,
                logger);
        }

        [Fact(Skip = "SetupIssue for dynamic")]
        public async void GivenAName_WhenGettingIdentityProvider_ThenIdentityProviderReturned()
        {
            var identityProvider = await _controlPlaneDataStore.GetIdentityProviderAsync("aad", CancellationToken.None);

            Assert.Equal(_cosmosIdentityProvider.Name, identityProvider.Name);
        }
    }
}
