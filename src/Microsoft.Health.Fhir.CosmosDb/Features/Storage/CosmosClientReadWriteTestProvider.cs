// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
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

        public async Task PerformTestAsync(
            Container container,
            CosmosDataStoreConfiguration configuration,
            CosmosCollectionConfiguration cosmosCollectionConfiguration,
            CancellationToken cancellationToken)
        {
            var requestOptions = new ItemRequestOptions
            {
                EnableContentResponseOnWrite = false,
                ConsistencyLevel = ConsistencyLevel.Session,
            };

            var resourceResponse = await container.UpsertItemAsync(
                _document,
                _partitionKey,
                requestOptions,
                cancellationToken);

            requestOptions.SessionToken = resourceResponse.Headers.Session;

            await container.ReadItemAsync<HealthCheckDocument>(_document.Id, _partitionKey, requestOptions, cancellationToken);
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
