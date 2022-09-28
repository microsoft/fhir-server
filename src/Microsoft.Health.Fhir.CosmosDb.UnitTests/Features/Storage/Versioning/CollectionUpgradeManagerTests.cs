// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Versioning
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class CollectionUpgradeManagerTests
    {
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration
        {
            AllowDatabaseCreation = false,
            ConnectionMode = ConnectionMode.Direct,
            DatabaseId = "testdatabaseid",
            Host = "https://fakehost",
            Key = "ZmFrZWtleQ==",   // "fakekey"
            PreferredLocations = null,
        };

        private readonly CosmosCollectionConfiguration _cosmosCollectionConfiguration = new CosmosCollectionConfiguration
        {
            CollectionId = "testcollectionid",
        };

        private readonly CollectionUpgradeManager _manager;
        private readonly Container _client;
        private readonly ContainerResponse _containerResponse;

        public CollectionUpgradeManagerTests()
        {
            var factory = Substitute.For<ICosmosDbDistributedLockFactory>();
            var cosmosDbDistributedLock = Substitute.For<ICosmosDbDistributedLock>();
            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();

            optionsMonitor.Get(Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            factory.Create(Arg.Any<Container>(), Arg.Any<string>()).Returns(cosmosDbDistributedLock);
            cosmosDbDistributedLock.TryAcquireLock().Returns(true);

            _client = Substitute.For<Container>();

            var collectionVersionWrappers = Substitute.ForPartsOf<FeedIterator<CollectionVersion>>();

            _client.GetItemQueryIterator<CollectionVersion>(Arg.Any<QueryDefinition>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
                .Returns(collectionVersionWrappers);

            collectionVersionWrappers.ReadNextAsync(Arg.Any<CancellationToken>())
                .Returns(Substitute.ForPartsOf<FeedResponse<CollectionVersion>>());

            var updaters = new ICollectionUpdater[] { new FhirCollectionSettingsUpdater(_cosmosDataStoreConfiguration, optionsMonitor, NullLogger<FhirCollectionSettingsUpdater>.Instance), };
            _manager = new CollectionUpgradeManager(updaters, _cosmosDataStoreConfiguration, optionsMonitor, factory, NullLogger<CollectionUpgradeManager>.Instance);

            _containerResponse = Substitute.ForPartsOf<ContainerResponse>();

            var containerProperties = new ContainerProperties();
            containerProperties.IndexingPolicy = new IndexingPolicy
            {
                IncludedPaths = { },
                ExcludedPaths = { },
            };

            _containerResponse.Resource.Returns(containerProperties);
            _client.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(_containerResponse);
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionIndexIsUpdated()
        {
            await UpdateCollectionAsync();

            await _client.Received(1).ReplaceContainerAsync(Arg.Any<ContainerProperties>(), null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionVersionWrapperIsSaved()
        {
            await UpdateCollectionAsync();

            await _client.Received(1)
                .UpsertItemAsync(Arg.Is<CollectionVersion>(x => x.Version == _manager.CollectionSettingsVersion), Arg.Any<PartitionKey?>(), null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionTTLIsSetToNeg1()
        {
            await UpdateCollectionAsync();

            Assert.Equal(-1, _containerResponse.Resource.DefaultTimeToLive);
        }

        private async Task UpdateCollectionAsync()
        {
            await _manager.SetupContainerAsync(_client);
        }
    }
}
