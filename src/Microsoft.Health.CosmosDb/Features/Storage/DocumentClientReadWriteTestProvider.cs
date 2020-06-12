// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection.Metadata;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
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

        public async Task PerformTest(Container documentClient, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration)
        {
            var requestOptions = new ItemRequestOptions { ConsistencyLevel = ConsistencyLevel.Session };

            var resourceResponse = await documentClient.UpsertItemAsync(
                _document,
                _partitionKey,
                requestOptions);

            requestOptions.SessionToken = resourceResponse.Headers.Session;

            await documentClient.ReadItemAsync<HealthCheckDocument>(resourceResponse.Resource.Id, _partitionKey, requestOptions);
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
