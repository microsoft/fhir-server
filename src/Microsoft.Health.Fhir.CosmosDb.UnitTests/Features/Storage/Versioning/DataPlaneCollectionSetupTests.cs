// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning;
using Microsoft.Health.Fhir.CosmosDb.Initialization.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Versioning
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class DataPlaneCollectionSetupTests
    {
        private readonly Container _container;
        private readonly DataPlaneCollectionSetup _setup;
        private readonly ContainerResponse _containerResponse;
        private readonly ILogger<DataPlaneCollectionSetup> _logger = Substitute.For<ILogger<DataPlaneCollectionSetup>>();
        private readonly CollectionVersion _version;

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

        public DataPlaneCollectionSetupTests()
        {
            _container = Substitute.For<Container>();
            _containerResponse = Substitute.ForPartsOf<ContainerResponse>();
            _version = new CollectionVersion();

            var containerProperties = new ContainerProperties();
            containerProperties.IndexingPolicy = new IndexingPolicy
            {
                IncludedPaths = { },
                ExcludedPaths = { },
            };

            _containerResponse.Resource.Returns(containerProperties);
            _container.ReadContainerAsync(Arg.Any<ContainerRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(_containerResponse);

            var optionsMonitor = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();
            optionsMonitor.Get(Constants.CollectionConfigurationName).Returns(_cosmosCollectionConfiguration);

            var collectionInitializer = Substitute.For<ICosmosClientInitializer>();
            collectionInitializer.CreateFhirContainer(Arg.Any<CosmosClient>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(_container);

            var collectionVersionWrappers = Substitute.ForPartsOf<FeedIterator<CollectionVersion>>();

            _container.GetItemQueryIterator<CollectionVersion>(Arg.Any<QueryDefinition>(), Arg.Any<string>(), Arg.Any<QueryRequestOptions>())
                .Returns(collectionVersionWrappers);

            collectionVersionWrappers.ReadNextAsync(Arg.Any<CancellationToken>())
                .Returns(Substitute.ForPartsOf<FeedResponse<CollectionVersion>>());

            var storeProcedureInstaller = Substitute.For<IStoredProcedureInstaller>();
            storeProcedureInstaller.ExecuteAsync(Arg.Any<Container>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(true));

            _setup = new DataPlaneCollectionSetup(_cosmosDataStoreConfiguration, optionsMonitor, collectionInitializer, storeProcedureInstaller, _logger);
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionIndexIsUpdated()
        {
            await _setup.UpdateFhirCollectionSettingsAsync(_version, CancellationToken.None);
            await _container.Received(1).ReplaceContainerAsync(Arg.Any<ContainerProperties>(), null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenACollection_WhenSettingUpCollection_ThenTheCollectionTTLIsSetToNeg1()
        {
            await _setup.UpdateFhirCollectionSettingsAsync(_version, CancellationToken.None);
            Assert.Equal(-1, _containerResponse.Resource.DefaultTimeToLive);
        }
    }
}
