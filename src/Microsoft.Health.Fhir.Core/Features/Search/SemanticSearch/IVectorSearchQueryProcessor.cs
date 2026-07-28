// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Prepares parsed vector search expressions for execution against vector storage.
    /// </summary>
    public interface IVectorSearchQueryProcessor
    {
        /// <summary>
        /// Generates the embedding and resolves model provenance for a parsed vector search expression.
        /// </summary>
        /// <param name="expression">The parsed FHIR search expression, or <see langword="null"/> when no filters were supplied.</param>
        /// <param name="cancellationToken">A token used to cancel embedding generation and model resolution.</param>
        /// <returns>The prepared query, or <see langword="null"/> when the expression contains no vector search.</returns>
        Task<PreparedVectorSearchQuery> PrepareAsync(Expression expression, CancellationToken cancellationToken);
    }
}
