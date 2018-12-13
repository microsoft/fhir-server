// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class ControlPlaneCollectionInitializer : CollectionInitializer
    {
        public ControlPlaneCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));

            CollectionId = cosmosDataStoreConfiguration.ControlPlaneCollectionId;
            RelativeCollectionUri = cosmosDataStoreConfiguration.RelativeControlPlaneCollectionUri;
            RelativeDatabaseUri = cosmosDataStoreConfiguration.RelativeDatabaseUri;
        }
    }
}
