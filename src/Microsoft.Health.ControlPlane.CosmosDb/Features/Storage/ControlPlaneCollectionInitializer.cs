// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class ControlPlaneCollectionInitializer : CollectionInitializer
    {
        private ControlPlaneCollectionUpgradeManager _controlPlaneCollectionUpgradeManager;

        public ControlPlaneCollectionInitializer(CosmosDataStoreConfiguration cosmosDataStoreConfiguration, ControlPlaneCollectionUpgradeManager controlPlaneCollectionUpgradeManager)
        {
            EnsureArg.IsNotNull(cosmosDataStoreConfiguration, nameof(cosmosDataStoreConfiguration));
            EnsureArg.IsNotNull(controlPlaneCollectionUpgradeManager, nameof(controlPlaneCollectionUpgradeManager));

            CollectionId = cosmosDataStoreConfiguration.ControlPlaneCollectionId;
            RelativeCollectionUri = cosmosDataStoreConfiguration.RelativeControlPlaneCollectionUri;
            RelativeDatabaseUri = cosmosDataStoreConfiguration.RelativeDatabaseUri;
            _controlPlaneCollectionUpgradeManager = controlPlaneCollectionUpgradeManager;
        }

        public override async Task<DocumentCollection> InitializeCollection(IDocumentClient documentClient)
        {
            var documentCollection = await base.InitializeCollection(documentClient);

            await _controlPlaneCollectionUpgradeManager.SetupCollectionAsync(documentClient, documentCollection);

            return documentCollection;
        }
    }
}
