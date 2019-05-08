// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbFhirStorageTestHelper : IFhirStorageTestHelper
    {
        private const string ExportJobPartitionKey = "ExportJob";

        private readonly IDocumentClient _documentClient;
        private readonly Uri _collectionUri;

        public CosmosDbFhirStorageTestHelper(
            IDocumentClient documentClient,
            Uri collectionUri)
        {
            _documentClient = documentClient;
            _collectionUri = collectionUri;
        }

        public async Task DeleteAllExportJobRecordsAsync()
        {
            IDocumentQuery<Document> query = _documentClient.CreateDocumentQuery<Document>(
                _collectionUri,
                new SqlQuerySpec("SELECT doc._self FROM doc"),
                new FeedOptions() { PartitionKey = new PartitionKey(ExportJobPartitionKey) })
                .AsDocumentQuery();

            while (query.HasMoreResults)
            {
                FeedResponse<Document> documents = await query.ExecuteNextAsync<Document>();

                foreach (Document doc in documents)
                {
                    await _documentClient.DeleteDocumentAsync(doc.SelfLink, new RequestOptions() { PartitionKey = new PartitionKey(ExportJobPartitionKey) });
                }
            }
        }
    }
}
