// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirDataStoreContext : IFhirDataStoreContext
    {
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration;
        private readonly CosmosCollectionConfiguration _collectionConfiguration;

        public FhirDataStoreContext(
            CosmosDataStoreConfiguration cosmosDataStoreConfiguration,
            IOptionsMonitor<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(namedCosmosCollectionConfigurationAccessor, nameof(namedCosmosCollectionConfigurationAccessor));

            _collectionConfiguration = namedCosmosCollectionConfigurationAccessor.Get(Constants.CollectionConfigurationName);

            _cosmosDataStoreConfiguration = cosmosDataStoreConfiguration;
            CollectionUri = cosmosDataStoreConfiguration.GetRelativeCollectionUri(_collectionConfiguration.CollectionId);
        }

        public string DatabaseId => _cosmosDataStoreConfiguration.DatabaseId;

        public string CollectionId => _collectionConfiguration.CollectionId;

        public Uri CollectionUri { get; }

        public int? ContinuationTokenSizeLimitInKb => _cosmosDataStoreConfiguration.ContinuationTokenSizeLimitInKb;
    }
}
