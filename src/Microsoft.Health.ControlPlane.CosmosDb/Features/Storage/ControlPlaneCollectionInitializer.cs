// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage
{
    public class ControlPlaneCollectionInitializer : CollectionInitializer
    {
        public ControlPlaneCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration, ControlPlaneCollectionUpgradeManager controlPlaneCollectionUpgradeManager, ILogger<ControlPlaneCollectionInitializer> logger)
            : base(
                cosmosDataStoreConfiguration.ControlPlaneCollectionId,
                cosmosDataStoreConfiguration.RelativeDatabaseUri,
                cosmosDataStoreConfiguration.RelativeControlPlaneCollectionUri,
                cosmosDataStoreConfiguration.InitialControlPlaneCollectionThroughput,
                controlPlaneCollectionUpgradeManager,
                logger)
        {
        }
    }
}
