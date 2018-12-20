// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Versioning
{
    public class ControlPlaneCollectionUpgradeManager : CollectionUpgradeManager
    {
        public ControlPlaneCollectionUpgradeManager(
            IEnumerable<IControlPlaneCollectionUpdater> collectionUpdater,
            CosmosDataStoreConfiguration configuration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosDbDistributedLockFactory lockFactory,
            ILogger<ControlPlaneCollectionUpgradeManager> logger)
            : base(
                collectionUpdater,
                configuration,
                GetCosmosCollectionConfiguration(namedCosmosCollectionConfigurationAccessor, Constants.CollectionConfigurationName),
                lockFactory,
                logger)
        {
        }

        public override int CollectionSettingsVersion => 1;
    }
}
