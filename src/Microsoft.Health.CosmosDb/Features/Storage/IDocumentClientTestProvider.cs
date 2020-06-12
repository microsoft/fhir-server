// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.CosmosDb.Configs;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public interface IDocumentClientTestProvider
    {
        Task PerformTest(Container documentClient, CosmosDataStoreConfiguration configuration, CosmosCollectionConfiguration cosmosCollectionConfiguration);
    }
}
