// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Queries
{
    public interface ICosmosQuery<TResult>
    {
        bool HasMoreResults { get; }

        Task<FeedResponse<TResult>> ExecuteNextAsync(CancellationToken token = default);
    }
}
