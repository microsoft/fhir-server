// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCollectionInitializer : CollectionInitializer
    {
        public FhirCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration, FhirCollectionUpgradeManager fhirCollectionUpgradeManager, ILogger<FhirCollectionInitializer> logger)
            : base(
                cosmosDataStoreConfiguration.FhirCollectionId,
                cosmosDataStoreConfiguration,
                cosmosDataStoreConfiguration.InitialFhirCollectionThroughput,
                fhirCollectionUpgradeManager,
                logger)
        {
        }
    }
}
