// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence;

public class CosmosDbFhirExportTestFixture : CosmosDbFhirStorageTestsFixture, IAsyncLifetime
{
    public CosmosDbFhirExportTestFixture()
        : base()
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        // 1111 patients + 1111 observations + 1111 claims.
        await PrepareData(3333);
    }

    public override async Task DisposeAsync()
    {
        await CleanupTestResourceDocuments();
        await base.DisposeAsync();
    }

    private async Task PrepareData(int numberOfResourcesToGenerate)
    {
        await CleanupTestResourceDocuments();

        string[] resourceTypes = ["Patient", "Observation", "Claim"];

        for (int i = 0; i < numberOfResourcesToGenerate; i++)
        {
            string id = Guid.NewGuid().ToString();
            string resourceType = resourceTypes[i % resourceTypes.Length];

            RawResource rawResource = new(
                $"{{\"resourceType\" = \"{resourceType}\", \"id\"=\"{id}\"}}",
                FhirResourceFormat.Json,
                false);

            var resourceWrapper = new FhirCosmosResourceWrapper(
                id,
                "1",
                resourceType,
                rawResource,
                default,
                DateTimeOffset.Now.AddMinutes(-i),
                false,
                false,
                default,
                default,
                default,
                default);

            await Container.CreateItemAsync(resourceWrapper);
        }
    }

    private async Task CleanupTestResourceDocuments()
    {
        var partitionKey = JobGroupWrapper.GetJobInfoPartitionKey((byte)QueueType.Export);
        List<QueryDefinition> queries =
            [
                new(@"SELECT c.id FROM root c WHERE c.partitionKey = '" + partitionKey + @"'"),
                new(@"SELECT c.id FROM root c WHERE IS_DEFINED(c.resourceTypeName) AND c.isSystem = false"),
            ];
        var queryOptions = new QueryRequestOptions { MaxItemCount = -1 }; // Set the max item count to -1 to fetch all items

        foreach (QueryDefinition query in queries)
        {
            using FeedIterator<dynamic> resultSet = Container.GetItemQueryIterator<dynamic>(query, requestOptions: queryOptions);

            var batch = Container.CreateTransactionalBatch(new PartitionKey(string.Empty));

            while (resultSet.HasMoreResults)
            {
                foreach (dynamic response in await resultSet.ReadNextAsync())
                {
                    batch.DeleteItem(response.id.ToString());
                }
            }
        }
    }
}
