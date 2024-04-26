// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using DotLiquid.Util;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Versioning
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
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
        private readonly Container _container;
        private readonly ContainerResponse _containerResponse;

        public CollectionUpgradeManagerTests()
        {
            _container = Substitute.For<Container>();

            var cosmosDbDistributedLock = Substitute.For<ICosmosDbDistributedLock>();
            cosmosDbDistributedLock.TryAcquireLock().Returns(true);

            var factory = Substitute.For<ICosmosDbDistributedLockFactory>();
            factory.Create(Arg.Any<Container>(), Arg.Any<string>()).Returns(cosmosDbDistributedLock);

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();
            optionsMonitor.Get(Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            var collectionDataUpdater = Substitute.For<ICollectionDataUpdater>();
            collectionDataUpdater.ExecuteAsync(Arg.Any<Container>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));

            var storeProcedureInstaller = Substitute.For<IStoredProcedureInstaller>();
            storeProcedureInstaller.ExecuteAsync(Arg.Any<Container>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));

            var collectionInitializer = Substitute.For<ICosmosClientInitializer>();
            collectionInitializer.CreateFhirContainer(Arg.Any<CosmosClient>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(_container);

            var dataPlaneCollectionSetup = new DataPlaneCollectionSetup(
                _cosmosDataStoreConfiguration,
                optionsMonitor,
                collectionInitializer,
                NullLogger<DataPlaneCollectionSetup>.Instance);

            var collectionVersionWrappers = Substitute.ForPartsOf<FeedIterator<CollectionVersion>>();

            _container.GetItemQueryIterator<CollectionVersion>(Arg.Any<QueryDefinition>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
                .Returns(collectionVersionWrappers);

            collectionVersionWrappers.ReadNextAsync(Arg.Any<CancellationToken>())
                .Returns(Substitute.ForPartsOf<FeedResponse<CollectionVersion>>());

            _manager = new CollectionUpgradeManager(
                collectionDataUpdater,
                storeProcedureInstaller,
                dataPlaneCollectionSetup,
                _cosmosDataStoreConfiguration,
                optionsMonitor,
                factory,
                NullLogger<CollectionUpgradeManager>.Instance);

            _containerResponse = Substitute.ForPartsOf<ContainerResponse>();

            var containerProperties = new ContainerProperties();
            containerProperties.IndexingPolicy = new IndexingPolicy
            {
                IncludedPaths = { },
                ExcludedPaths = { },
            };

            _containerResponse.Resource.Returns(containerProperties);
            _container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(_containerResponse);
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionIndexIsUpdated()
        {
            await UpdateCollectionAsync();

            await _container.Received(1).ReplaceContainerAsync(Arg.Any<ContainerProperties>(), null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionVersionWrapperIsSaved()
        {
            await UpdateCollectionAsync();

            await _container.Received(1)
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
            await _manager.SetupContainerAsync(_container, cancellationToken: GetCancellationToken());
        }

        private static CancellationToken GetCancellationToken()
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return tokenSource.Token;
        }
    }
}
