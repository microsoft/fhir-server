// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public static class CosmosClientExtensions
    {
        public static async Task<ContainerResponse> TryGetContainerAsync(
            this Database cosmosClient,
            string collectionId)
        {
            try
            {
                return await cosmosClient.GetContainer(collectionId).ReadContainerAsync();
            }
            catch (CosmosException readException) when (readException.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }
    }
}
