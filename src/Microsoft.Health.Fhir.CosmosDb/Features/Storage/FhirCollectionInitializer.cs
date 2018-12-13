// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class FhirCollectionInitializer : CollectionInitializer
    {
        private readonly FhirCollectionUpgradeManager _fhirCollectionUpgradeManager;

        public FhirCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration, FhirCollectionUpgradeManager fhirCollectionUpgradeManager)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(fhirCollectionUpgradeManager, nameof(fhirCollectionUpgradeManager));

            CollectionId = cosmosDataStoreConfiguration.FhirCollectionId;
            RelativeCollectionUri = cosmosDataStoreConfiguration.RelativeFhirCollectionUri;
            RelativeDatabaseUri = cosmosDataStoreConfiguration.RelativeDatabaseUri;

            _fhirCollectionUpgradeManager = fhirCollectionUpgradeManager;
        }

        public override async Task<DocumentCollection> InitializeCollection(IDocumentClient documentClient)
        {
            var documentCollection = await base.InitializeCollection(documentClient);

            await _fhirCollectionUpgradeManager.SetupCollectionAsync(documentClient, documentCollection);

            return documentCollection;
        }
    }
}
