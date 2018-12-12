// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;

namespace Microsoft.Health.CosmosDb.Features.Storage.Versioning
{
    public class CollectionUpgradeManager : IUpgradeManager
    {
        private readonly IEnumerable<ICollectionUpdater> _collectionUpdater;
        private readonly CosmosDataStoreConfiguration _configuration;

        /// <summary>
        /// This integer should be incremented when changing any value in the
        /// UpdateIndexAsync function
        /// </summary>
        public const int CollectionSettingsVersion = 1; // TODO: Determine if this should be internal or public (originally internal)

        private readonly ICosmosDbDistributedLockFactory _lockFactory;
        private readonly ILogger<CollectionUpgradeManager> _logger;

        public CollectionUpgradeManager(
            IEnumerable<ICollectionUpdater> collectionUpdater,
            CosmosDataStoreConfiguration configuration,
            ICosmosDbDistributedLockFactory lockFactory,
            ILogger<CollectionUpgradeManager> logger)
        {
            EnsureArg.IsNotNull(collectionUpdater, nameof(collectionUpdater));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(lockFactory, nameof(lockFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _collectionUpdater = collectionUpdater;
            _configuration = configuration;
            _lockFactory = lockFactory;
            _logger = logger;
        }

        public async Task SetupCollectionAsync(IDocumentClient documentClient, DocumentCollection collection)
        {
            EnsureArg.IsNotNull(documentClient, nameof(documentClient));
            EnsureArg.IsNotNull(collection, nameof(collection));

            using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            {
                using (var distributedLock = _lockFactory.Create(documentClient, _configuration.RelativeFhirCollectionUri, $"UpgradeLock:{CollectionSettingsVersion}"))
                {
                    _logger.LogDebug("Attempting to acquire upgrade lock");

                    await distributedLock.AcquireLock(cancellationTokenSource.Token);

                    foreach (var updater in _collectionUpdater)
                    {
                        _logger.LogDebug("Running {CollectionUpdater} on {CollectionUri}", updater.GetType().Name, _configuration.AbsoluteFhirCollectionUri);

                        await updater.ExecuteAsync(documentClient, collection, _configuration.RelativeFhirCollectionUri);
                    }

                    await distributedLock.ReleaseLock();
                }
            }
        }
    }
}
