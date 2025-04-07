// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.StoredProcedures;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage.Versioning
{
    public class CollectionUpgradeManager : IUpgradeManager
    {
        private readonly ICollectionDataUpdater _collectionDataUpdater;
        private readonly CosmosDataStoreConfiguration _configuration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;
        private readonly ICosmosDbDistributedLockFactory _lockFactory;
        private readonly ILogger<CollectionUpgradeManager> _logger;

        public CollectionUpgradeManager(
            ICollectionDataUpdater collectionDataUpdater,
            CosmosDataStoreConfiguration configuration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosDbDistributedLockFactory lockFactory,
            ILogger<CollectionUpgradeManager> logger)
        {
            EnsureArg.IsNotNull(collectionDataUpdater, nameof(collectionDataUpdater));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));
            EnsureArg.IsNotNull(lockFactory, nameof(lockFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));
            _collectionDataUpdater = collectionDataUpdater;
            _configuration = configuration;
            _collectionConfiguration = GetCosmosCollectionConfiguration(namedCosmosCollectionConfigurationAccessor, Constants.CollectionConfigurationName);
            _lockFactory = lockFactory;
            _logger = logger;
        }

        /// <summary>
        /// This integer should be incremented in the derived instance when changing any configuration in the derived CollectionUpgradeManager
        /// </summary>
        public int CollectionSettingsVersion { get; } = 3;

        public async Task SetupContainerAsync(Container container, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(container, nameof(container));

            using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            {
                await using (var distributedLock = _lockFactory.Create(container, $"UpgradeLock:{CollectionSettingsVersion}"))
                {
                    _logger.LogDebug("Attempting to acquire upgrade lock");

                    await distributedLock.AcquireLock(cancellationTokenSource.Token);
                    await _collectionDataUpdater.ExecuteAsync(container, cancellationToken);
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
