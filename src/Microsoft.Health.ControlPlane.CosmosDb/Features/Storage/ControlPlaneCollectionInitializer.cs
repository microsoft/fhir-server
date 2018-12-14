// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class ControlPlaneCollectionInitializer : CollectionInitializer
    {
        public ControlPlaneCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration, ControlPlaneCollectionUpgradeManager controlPlaneCollectionUpgradeManager)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(controlPlaneCollectionUpgradeManager, nameof(controlPlaneCollectionUpgradeManager));

            CollectionId = cosmosDataStoreConfiguration.ControlPlaneCollectionId;
            InitialCollectionThroughput = cosmosDataStoreConfiguration.InitialControlPlaneCollectionThroughput;
            RelativeCollectionUri = cosmosDataStoreConfiguration.RelativeControlPlaneCollectionUri;
            RelativeDatabaseUri = cosmosDataStoreConfiguration.RelativeDatabaseUri;
            UpgradeManager = controlPlaneCollectionUpgradeManager;
        }
    }
}
