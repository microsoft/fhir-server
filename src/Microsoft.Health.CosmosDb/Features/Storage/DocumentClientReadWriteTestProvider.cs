// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Health.CosmosDb.Configs;
using Newtonsoft.Json;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class DocumentClientReadWriteTestProvider : IDocumentClientTestProvider
    {
        private readonly HealthCheckDocument _document;
        private readonly PartitionKey _partitionKey;

        public DocumentClientReadWriteTestProvider()
        {
            _document = new HealthCheckDocument();
            _partitionKey = new PartitionKey(_document.PartitionKey);
        }

        public async Task PerformTest(IDocumentClient documentClient, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration)
        {
            var requestOptions = new RequestOptions { ConsistencyLevel = ConsistencyLevel.Session, PartitionKey = _partitionKey };

            ResourceResponse<Document> resourceResponse = await documentClient.UpsertDocumentAsync(
                configuration.GetRelativeCollectionUri(cosmosCollectionConfiguration.CollectionId),
                _document,
                requestOptions);

            requestOptions.SessionToken = resourceResponse.SessionToken;

            await documentClient.ReadDocumentAsync(resourceResponse.Resource.SelfLink, requestOptions);
        }

        private class HealthCheckDocument : SystemData
        {
            public HealthCheckDocument()
            {
                Id = "__healthcheck__";
            }

            [JsonProperty(KnownDocumentProperties.PartitionKey)]
            public string PartitionKey { get; } = "__healthcheck__";
        }
    }
}
