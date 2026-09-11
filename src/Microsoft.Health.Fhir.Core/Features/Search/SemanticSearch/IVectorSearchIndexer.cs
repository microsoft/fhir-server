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
        /// Adds vector search indices to the supplied resource wrappers.
        /// </summary>
        /// <param name="resources">The resources to index.</param>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns>A task representing the indexing operation.</returns>
        Task IndexAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken);
    }
}
