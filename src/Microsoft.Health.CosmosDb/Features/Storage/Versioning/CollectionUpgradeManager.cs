// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;

namespace Microsoft.Health.CosmosDb.Features.Storage.Versioning
{
    public abstract class CollectionUpgradeManager : IUpgradeManager
    {
        private readonly IEnumerable<ICollectionUpdater> _collectionUpdater;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;
        private readonly ICosmosDbDistributedLockFactory _lockFactory;
        private readonly ILogger<CollectionUpgradeManager> _logger;

        protected CollectionUpgradeManager(
            IEnumerable<ICollectionUpdater> collectionUpdater,
            CosmosDataStoreConfiguration configuration,
            CosmosCollectionConfiguration cosmosCollectionConfiguration,
            ICosmosDbDistributedLockFactory lockFactory,
            ILogger<CollectionUpgradeManager> logger)
        {
            EnsureArg.IsNotNull(collectionUpdater, nameof(collectionUpdater));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(cosmosCollectionConfiguration, nameof(cosmosCollectionConfiguration));
            EnsureArg.IsNotNull(lockFactory, nameof(lockFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _collectionUpdater = collectionUpdater;
            _configuration = configuration;
            _collectionConfiguration = cosmosCollectionConfiguration;
            _lockFactory = lockFactory;
            _logger = logger;
        }

        /// <summary>
        /// This integer should be incremented in the derived instance when changing any configuration in the derived CollectionUpgradeManager
        /// </summary>
        public abstract int CollectionSettingsVersion { get; }

        public async Task SetupCollectionAsync(IDocumentClient documentClient, DocumentCollection collection)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(collection, nameof(collection));

            using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            {
                using (var distributedLock = _lockFactory.Create(documentClient, _configuration.GetRelativeCollectionUri(_collectionConfiguration.CollectionId), $"UpgradeLock:{CollectionSettingsVersion}"))
                {
                    _logger.LogDebug("Attempting to acquire upgrade lock");

                    await distributedLock.AcquireLock(cancellationTokenSource.Token);

                    foreach (var updater in _collectionUpdater)
                    {
                        _logger.LogDebug("Running {CollectionUpdater} on {CollectionUri}", updater.GetType().Name, _configuration.GetAbsoluteCollectionUri(_collectionConfiguration.CollectionId));

                        await updater.ExecuteAsync(documentClient, collection, _configuration.GetRelativeCollectionUri(_collectionConfiguration.CollectionId));
                    }

                    await distributedLock.ReleaseLock();
                }
            }
        }

        protected static CosmosCollectionConfiguration GetCosmosCollectionConfiguration(
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            string collectionConfigurationName)
        {
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNullOrWhiteSpace(collectionConfigurationName, nameof(collectionConfigurationName));

            return namedCosmosCollectionConfigurationAccessor.Get(collectionConfigurationName);
        }
    }
}
