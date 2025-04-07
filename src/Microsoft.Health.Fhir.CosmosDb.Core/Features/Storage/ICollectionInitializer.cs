// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage
{
    public interface ICollectionInitializer
    {
        Task<Container> InitializeCollectionAsync(CosmosClient client, AsyncPolicy retryPolicy, CancellationToken cancellationToken = default);
    }
}
