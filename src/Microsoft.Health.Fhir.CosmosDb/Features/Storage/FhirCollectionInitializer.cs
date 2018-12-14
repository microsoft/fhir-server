// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCollectionInitializer : CollectionInitializer
    {
        public FhirCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration, FhirCollectionUpgradeManager fhirCollectionUpgradeManager)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(fhirCollectionUpgradeManager, nameof(fhirCollectionUpgradeManager));

            CollectionId = cosmosDataStoreConfiguration.FhirCollectionId;
            InitialCollectionThroughput = cosmosDataStoreConfiguration.InitialFhirCollectionThroughput;
            RelativeCollectionUri = cosmosDataStoreConfiguration.RelativeFhirCollectionUri;
            RelativeDatabaseUri = cosmosDataStoreConfiguration.RelativeDatabaseUri;
            UpgradeManager = fhirCollectionUpgradeManager;
        }
    }
}
