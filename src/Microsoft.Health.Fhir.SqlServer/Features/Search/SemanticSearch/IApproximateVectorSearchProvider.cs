// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch
{
    /// <summary>
    /// Decides whether semantic queries may use approximate (DiskANN) vector retrieval.
    /// </summary>
    internal interface IApproximateVectorSearchProvider
    {
        /// <summary>
        /// Gets how many nearest chunks to examine per requested result when approximate retrieval applies.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The candidate multiplier when the setting is on and a compatible vector index exists;
        /// otherwise <see langword="null"/>, meaning queries must rank exactly.
        /// </returns>
        Task<int?> GetCandidateMultiplierAsync(CancellationToken cancellationToken);
    }
}
