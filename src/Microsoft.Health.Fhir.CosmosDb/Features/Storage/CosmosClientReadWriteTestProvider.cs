// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosClientReadWriteTestProvider : ICosmosClientTestProvider
    {
        private readonly HealthCheckDocument _document;
        private readonly PartitionKey _partitionKey;

        public CosmosClientReadWriteTestProvider()
        {
            _document = new HealthCheckDocument();
            _partitionKey = new PartitionKey(_document.PartitionKey);
        }

        public async Task PerformTest(Container container, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration)
        {
            var requestOptions = new ItemRequestOptions { ConsistencyLevel = ConsistencyLevel.Session };

            var resourceResponse = await container.UpsertItemAsync(
                _document,
                _partitionKey,
                requestOptions);

            requestOptions.SessionToken = resourceResponse.Headers.Session;

            await container.ReadItemAsync<HealthCheckDocument>(resourceResponse.Resource.Id, _partitionKey, requestOptions);
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
