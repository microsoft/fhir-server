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

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning
{
    public class FhirCollectionUpgradeManager : CollectionUpgradeManager
    {
        public FhirCollectionUpgradeManager(
            IEnumerable<IFhirCollectionUpdater> collectionUpdater,
            CosmosDataStoreConfiguration configuration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosDbDistributedLockFactory lockFactory,
            ILogger<FhirCollectionUpgradeManager> logger)
            : base(
                collectionUpdater,
                configuration,
                GetCosmosCollectionConfiguration(namedCosmosCollectionConfigurationAccessor, Constants.CollectionConfigurationName),
                lockFactory,
                logger)
        {
        }

        public override int CollectionSettingsVersion => 2;
    }
}
