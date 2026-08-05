// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Generates query embeddings inline for parsed vector search expressions.
    /// </summary>
    public sealed class VectorSearchQueryProcessor : IVectorSearchQueryProcessor
    {
        private readonly IEmbeddingClient _embeddingClient;
        private readonly IEmbeddingModelRegistry _embeddingModelRegistry;

        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchQueryProcessor"/> class.
        /// </summary>
        /// <param name="embeddingClient">The configured embedding client.</param>
        /// <param name="embeddingModelRegistry">The embedding model registry.</param>
        public VectorSearchQueryProcessor(
            IEmbeddingClient embeddingClient,
            IEmbeddingModelRegistry embeddingModelRegistry)
        {
            _embeddingClient = EnsureArg.IsNotNull(embeddingClient, nameof(embeddingClient));
            _embeddingModelRegistry = EnsureArg.IsNotNull(embeddingModelRegistry, nameof(embeddingModelRegistry));
        }

        /// <inheritdoc />
        public async Task<PreparedVectorSearchQuery> PrepareAsync(Expression expression, CancellationToken cancellationToken)
        {
            if (expression == null)
            {
                return null;
            }

            var collector = new VectorSearchExpressionCollector();
            expression.AcceptVisitor(collector, context: null);

            if (collector.Expressions.Count == 0)
            {
                return null;
            }

            if (collector.Expressions.Count > 1)
            {
                throw new InvalidSearchOperationException("Only one vector SearchParameter may be specified per search.");
            }

            if (_embeddingClient.Dimensions != VectorSearchConfiguration.SupportedDimensions)
            {
                throw new InvalidOperationException(
                    $"The embedding client produces {_embeddingClient.Dimensions} dimensions; expected {VectorSearchConfiguration.SupportedDimensions}.");
            }

            VectorSearchExpression vectorExpression = collector.Expressions[0];
            IReadOnlyList<float[]> embeddings = await _embeddingClient.GenerateEmbeddingsAsync(
                new[] { vectorExpression.QueryText },
                cancellationToken);

            if (embeddings.Count != 1)
            {
                throw new InvalidOperationException("The embedding service must return exactly one vector for a semantic query.");
            }

            float[] embedding = embeddings[0];
            if (embedding == null || embedding.Length != _embeddingClient.Dimensions)
            {
                int actualDimensions = embedding?.Length ?? 0;
                throw new InvalidOperationException(
                    $"The embedding service returned a vector with {actualDimensions} dimensions; expected {_embeddingClient.Dimensions}.");
            }

            short embeddingModelId = await _embeddingModelRegistry.GetEmbeddingModelIdAsync(cancellationToken);
            return new PreparedVectorSearchQuery(
                vectorExpression.Parameter,
                embeddingModelId,
                embedding,
                vectorExpression.Parameter.VectorConfig.MinimumScore);
        }

        private sealed class VectorSearchExpressionCollector : DefaultExpressionVisitor<object, object>
        {
            public List<VectorSearchExpression> Expressions { get; } = new List<VectorSearchExpression>();

            public override object VisitVectorSearch(VectorSearchExpression expression, object context)
            {
                Expressions.Add(expression);
                return null;
            }
        }
    }
}
