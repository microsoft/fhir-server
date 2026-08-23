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
using Microsoft.Health.Fhir.Core.Models;

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

            CollectedVectorSearchExpression collectedExpression = collector.Expressions[0];
            VectorSearchExpression vectorExpression = collectedExpression.Expression;
            if (collectedExpression.ChainLinks.Count > 1)
            {
                throw new InvalidSearchOperationException("Semantic search currently supports one chain relationship.");
            }

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
                vectorExpression.Parameter.VectorConfig.MinimumScore,
                collectedExpression.ChainLinks);
        }

        private sealed class VectorSearchExpressionCollector : DefaultExpressionVisitor<object, object>
        {
            private readonly List<PreparedVectorSearchChainLink> _currentChainLinks = new List<PreparedVectorSearchChainLink>();

            public List<CollectedVectorSearchExpression> Expressions { get; } = new List<CollectedVectorSearchExpression>();

            public override object VisitChained(ChainedExpression expression, object context)
            {
                _currentChainLinks.Add(new PreparedVectorSearchChainLink(
                    expression.ResourceTypes,
                    expression.ReferenceSearchParameter,
                    expression.TargetResourceTypes,
                    expression.Reversed));

                try
                {
                    return expression.Expression.AcceptVisitor(this, context);
                }
                finally
                {
                    _currentChainLinks.RemoveAt(_currentChainLinks.Count - 1);
                }
            }

            public override object VisitVectorSearch(VectorSearchExpression expression, object context)
            {
                Expressions.Add(new CollectedVectorSearchExpression(expression, _currentChainLinks.ToArray()));
                return null;
            }
        }

        private sealed record CollectedVectorSearchExpression(
            VectorSearchExpression Expression,
            IReadOnlyList<PreparedVectorSearchChainLink> ChainLinks);
    }
}
