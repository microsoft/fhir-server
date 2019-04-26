// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbFhirStorageTestHelper : IFhirStorageTestHelper
    {
        private const string ExportJobPartitionKey = "ExportJob";

        private readonly IDocumentClient _documentClient;
        private readonly IFhirDataStoreContext _fhirDataStoreContext;

        public CosmosDbFhirStorageTestHelper(IDocumentClient documentClient, IFhirDataStoreContext fhirDataStoreContext)
        {
            _documentClient = documentClient;
            _fhirDataStoreContext = fhirDataStoreContext;
        }

        public async Task DeleteAllExportJobRecordsAsync()
        {
            IDocumentQuery<Document> query = _documentClient.CreateDocumentQuery<Document>(
                _fhirDataStoreContext.CollectionUri,
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
