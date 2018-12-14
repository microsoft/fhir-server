// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class CollectionInitializer : ICollectionInitializer
    {
        public string CollectionId { get; set; }

        public Uri RelativeDatabaseUri { get; set; }

        public Uri RelativeCollectionUri { get; set; }

        public int? InitialCollectionThroughput { get; set; }

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

            return existingDocumentCollection;
        }
    }
}
