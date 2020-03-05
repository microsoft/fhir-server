// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class CosmosDbFhirStorageTestHelper : IFhirStorageTestHelper
    {
        private const string ExportJobPartitionKey = "ExportJob";

        private readonly IDocumentClient _documentClient;
        private readonly Uri _collectionUri;
        private readonly string _databaseId;
        private readonly string _collectionId;

        public CosmosDbFhirStorageTestHelper(
            IDocumentClient documentClient,
            string databaseId,
            string collectionId)
        {
            _documentClient = documentClient;
            _databaseId = databaseId;
            _collectionId = collectionId;

            _collectionUri = UriFactory.CreateDocumentCollectionUri(_databaseId, _collectionId);
        }

        public async Task DeleteAllExportJobRecordsAsync(CancellationToken cancellationToken = default)
        {
            IDocumentQuery<Document> query = _documentClient.CreateDocumentQuery<Document>(
                _collectionUri,
                new SqlQuerySpec("SELECT doc._self FROM doc"),
                new FeedOptions() { PartitionKey = new PartitionKey(ExportJobPartitionKey) })
                .AsDocumentQuery();

            while (query.HasMoreResults)
            {
                FeedResponse<Document> documents = await query.ExecuteNextAsync<Document>(cancellationToken);

                foreach (Document doc in documents)
                {
                    await _documentClient.DeleteDocumentAsync(doc.SelfLink, new RequestOptions() { PartitionKey = new PartitionKey(ExportJobPartitionKey) }, cancellationToken);
                }
            }
        }

        public async Task DeleteExportJobRecordAsync(string id, CancellationToken cancellationToken = default)
        {
            Uri documentUri = UriFactory.CreateDocumentUri(_databaseId, _collectionId, id);
            await _documentClient.DeleteDocumentAsync(documentUri, new RequestOptions { PartitionKey = new PartitionKey(ExportJobPartitionKey) }, cancellationToken);
        }

        async Task<object> IFhirStorageTestHelper.GetSnapshotToken()
        {
            var documentQuery = _documentClient.CreateDocumentQuery(
                _collectionUri,
                "SELECT top 1 c._ts as Item1 FROM c ORDER BY c._ts DESC",
                new FeedOptions { EnableCrossPartitionQuery = true }).AsDocumentQuery();

            while (documentQuery.HasMoreResults)
            {
                foreach (Tuple<int> ts in await documentQuery.ExecuteNextAsync<Tuple<int>>())
                {
                    return ts.Item1;
                }
            }

            return null;
        }

        async Task IFhirStorageTestHelper.ValidateSnapshotTokenIsCurrent(object snapshotToken)
        {
            Assert.True((int)await ((IFhirStorageTestHelper)this).GetSnapshotToken() <= (int)snapshotToken);
        }
    }
}
