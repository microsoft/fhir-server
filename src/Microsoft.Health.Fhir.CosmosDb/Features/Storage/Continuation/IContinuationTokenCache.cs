// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Continuation
{
    /// <summary>
    /// Defines a contract for caching and retrieving Cosmos DB continuation tokens
    /// </summary>
    /// <remarks>
    /// The larger a Cosmos DB collection grows, the bigger the Continuation Token gets.
    /// At a certain point, an encoded continuation token will exceed the accepted size of a URL (2048 chars),
    /// this breaks the Bundle.next link and results in an error (e.g. HTTP 414)
    ///
    /// This issue is also documented here:
    /// https://github.com/Azure/azure-documentdb-dotnet/issues/61
    /// https://github.com/Azure/azure-documentdb-dotnet/issues/330
    /// </remarks>
    public interface IContinuationTokenCache
    {
        /// <summary>
        /// Fetches the continuation token from a cached source
        /// </summary>
        /// <param name="id">The cache id for the continuation token</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The Continuation Token for CosmosDb</returns>
        Task<string> GetContinuationTokenAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a Continuation Token to a cache
        /// </summary>
        /// <param name="continuationToken">The Continuation Token from CosmosDb</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The id used for caching</returns>
        Task<string> SaveContinuationTokenAsync(string continuationToken, CancellationToken cancellationToken = default);
    }
}
