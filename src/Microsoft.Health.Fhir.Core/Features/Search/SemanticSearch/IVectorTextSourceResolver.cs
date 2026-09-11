// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves SearchParameter values to text and its FHIR provenance.
    /// </summary>
    public interface IVectorTextSourceResolver
    {
        /// <summary>
        /// Resolves extracted values to source text.
        /// </summary>
        /// <param name="owner">The resource being vector indexed.</param>
        /// <param name="searchParameter">The SearchParameter that extracted the values.</param>
        /// <param name="extractedValues">The extracted SearchParameter values.</param>
        /// <param name="writeBatch">Resources in the current write batch.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The resolved text sources.</returns>
        Task<IReadOnlyList<VectorTextSource>> ResolveAsync(
            ResourceWrapper owner,
            SearchParameterInfo searchParameter,
            IReadOnlyList<string> extractedValues,
            IReadOnlyCollection<ResourceWrapper> writeBatch,
            CancellationToken cancellationToken);
    }
}
