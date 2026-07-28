// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Contains the validated model and embedding inputs required to execute one vector search.
    /// </summary>
    public sealed class PreparedVectorSearchQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PreparedVectorSearchQuery"/> class.
        /// </summary>
        /// <param name="searchParameter">The vector SearchParameter being queried.</param>
        /// <param name="embeddingModelId">The database-local embedding model identifier.</param>
        /// <param name="embedding">The query embedding.</param>
        public PreparedVectorSearchQuery(
            SearchParameterInfo searchParameter,
            short embeddingModelId,
            IReadOnlyList<float> embedding)
        {
            SearchParameter = EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.IsNotNull(embedding, nameof(embedding));

            if (embedding.Count != VectorSearchConfiguration.SupportedDimensions)
            {
                throw new ArgumentException(
                    $"The query embedding must contain {VectorSearchConfiguration.SupportedDimensions} dimensions.",
                    nameof(embedding));
            }

            EmbeddingModelId = embeddingModelId;
            Embedding = Array.AsReadOnly(embedding.ToArray());
        }

        /// <summary>
        /// Gets the vector SearchParameter being queried.
        /// </summary>
        public SearchParameterInfo SearchParameter { get; }

        /// <summary>
        /// Gets the database-local embedding model identifier.
        /// </summary>
        public short EmbeddingModelId { get; }

        /// <summary>
        /// Gets an immutable copy of the query embedding.
        /// </summary>
        public IReadOnlyList<float> Embedding { get; }
    }
}
