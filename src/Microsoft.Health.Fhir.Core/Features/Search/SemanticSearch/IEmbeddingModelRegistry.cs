// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves the database identifier for the configured embedding model.
    /// </summary>
    public interface IEmbeddingModelRegistry
    {
        /// <summary>
        /// Gets the database-local identifier for the configured embedding model.
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns>The embedding model identifier.</returns>
        Task<short> GetEmbeddingModelIdAsync(CancellationToken cancellationToken);
    }
}
