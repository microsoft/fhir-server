// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Produces vector embeddings for text by calling an embedding model. Implementations may call an
    /// external endpoint (production) or generate vectors locally (tests).
    /// </summary>
    public interface IEmbeddingClient
    {
        /// <summary>
        /// Gets the number of dimensions in every embedding this client produces.
        /// </summary>
        int Dimensions { get; }

        /// <summary>
        /// Produces one embedding per input text.
        /// </summary>
        /// <param name="texts">The texts to embed. Must not be null.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>One vector per input text, each of length <see cref="Dimensions"/>, in the same order as <paramref name="texts"/>.</returns>
        Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
    }
}
