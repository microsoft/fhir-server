// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Contains the vector passages generated for one FHIR SearchParameter on one resource.
    /// </summary>
    public sealed class VectorSearchIndexEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchIndexEntry"/> class.
        /// </summary>
        /// <param name="searchParameter">The FHIR SearchParameter that extracted the source values.</param>
        /// <param name="embeddingModelId">The database-local embedding model identifier.</param>
        /// <param name="chunks">The ordered embedded passages.</param>
        public VectorSearchIndexEntry(
            SearchParameterInfo searchParameter,
            short embeddingModelId,
            IReadOnlyList<VectorSearchChunk> chunks)
        {
            SearchParameter = EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EmbeddingModelId = embeddingModelId;
            Chunks = EnsureArg.IsNotNull(chunks, nameof(chunks));
        }

        /// <summary>
        /// Gets the FHIR SearchParameter that extracted the source values.
        /// </summary>
        public SearchParameterInfo SearchParameter { get; }

        /// <summary>
        /// Gets the database-local embedding model identifier.
        /// </summary>
        public short EmbeddingModelId { get; }

        /// <summary>
        /// Gets the ordered embedded passages.
        /// </summary>
        public IReadOnlyList<VectorSearchChunk> Chunks { get; }
    }
}
