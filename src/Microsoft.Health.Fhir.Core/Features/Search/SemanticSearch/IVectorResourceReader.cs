// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Reads a persisted FHIR resource needed to resolve vector source text.
    /// </summary>
    public interface IVectorResourceReader
    {
        /// <summary>
        /// Reads the resource identified by <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The resource key.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The resource, or <see langword="null"/> when it does not exist.</returns>
        Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken);
    }
}
