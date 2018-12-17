// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class CollectionInitializer : ICollectionInitializer
    {
        public CollectionInitializer(string collectionId, Uri relativeDatabaseUri, Uri relativeCollectionUri, int? initialCollectionThroughput, IUpgradeManager upgradeManager)
        {
            EnsureArg.IsNotNull(collectionId, nameof(collectionId));
            EnsureArg.IsNotNull(relativeDatabaseUri, nameof(relativeDatabaseUri));
            EnsureArg.IsNotNull(relativeCollectionUri, nameof(relativeCollectionUri));
            EnsureArg.IsNotNull(upgradeManager, nameof(upgradeManager));

            CollectionId = collectionId;
            RelativeDatabaseUri = relativeDatabaseUri;
            RelativeCollectionUri = relativeCollectionUri;
            InitialCollectionThroughput = initialCollectionThroughput;
            UpgradeManager = upgradeManager;
        }

        public string CollectionId { get; set; }

        public Uri RelativeDatabaseUri { get; set; }

        public Uri RelativeCollectionUri { get; set; }

        public int? InitialCollectionThroughput { get; set; }

        public IUpgradeManager UpgradeManager { get; set; }

        public virtual async Task<DocumentCollection> InitializeCollection(IDocumentClient documentClient)
        {
            DocumentCollection existingDocumentCollection = await documentClient.TryGetDocumentCollectionAsync(RelativeCollectionUri);

            if (existingDocumentCollection == null)
            {
                var documentCollection = new DocumentCollection
                {
                    Id = CollectionId,
                    PartitionKey = new PartitionKeyDefinition
                    {
                        Paths =
                        {
                            $"/{KnownDocumentProperties.PartitionKey}",
                        },
                    },
                };

                var requestOptions = new RequestOptions { OfferThroughput = InitialCollectionThroughput };

                existingDocumentCollection = await documentClient.CreateDocumentCollectionIfNotExistsAsync(
                    RelativeDatabaseUri, RelativeCollectionUri, documentCollection, requestOptions);
            }

            await UpgradeManager.SetupCollectionAsync(documentClient, existingDocumentCollection);

            return existingDocumentCollection;
        }
    }
}
