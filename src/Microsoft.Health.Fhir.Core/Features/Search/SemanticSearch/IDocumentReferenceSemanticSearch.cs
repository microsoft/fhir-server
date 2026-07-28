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
    /// Ranks candidate DocumentReference resources using semantic similarity.
    /// </summary>
    public interface IDocumentReferenceSemanticSearch
    {
        /// <summary>
        /// Ranks already-authorized candidate resources for a natural-language query.
        /// </summary>
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            string query,
            IReadOnlyList<ResourceWrapper> candidates,
            int count,
            CancellationToken cancellationToken);
    }
}
