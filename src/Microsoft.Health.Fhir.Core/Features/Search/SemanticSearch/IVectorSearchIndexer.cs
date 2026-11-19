// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Generates vector search indices from ordinary FHIR SearchParameter extraction results.
    /// </summary>
    public interface IVectorSearchIndexer
    {
        /// <summary>
        /// Rebuilds and replaces vector search indices on the supplied resource wrappers for later persistence.
        /// </summary>
        /// <remarks>
        /// Marks each wrapper's vector indices as evaluated, including empty results that remove stale persisted indices.
        /// This method does not persist the resource or its indices.
        /// </remarks>
        /// <param name="resources">The resource wrappers whose vector search indices are updated.</param>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns>A task representing the in-memory vector index update.</returns>
        Task UpdateVectorSearchIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken);
    }
}
