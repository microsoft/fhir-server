// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a semantic query over a vector SearchParameter.
    /// </summary>
    public sealed class VectorSearchExpression : SearchParameterExpressionBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorSearchExpression"/> class.
        /// </summary>
        /// <param name="searchParameter">The vector SearchParameter to query.</param>
        /// <param name="queryText">The text for which a query embedding will be generated.</param>
        public VectorSearchExpression(SearchParameterInfo searchParameter, string queryText)
            : base(searchParameter)
        {
            EnsureArg.IsNotNullOrWhiteSpace(queryText, nameof(queryText));

            QueryText = queryText;
        }

        /// <summary>
        /// Gets the text for which a query embedding will be generated.
        /// </summary>
        public string QueryText { get; }

        /// <inheritdoc />
        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitVectorSearch(this, context);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"(Vector Param {Parameter.Code})";
        }

        /// <inheritdoc />
        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(VectorSearchExpression));
            hashCode.Add(Parameter);
        }

        /// <inheritdoc />
        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is VectorSearchExpression vectorExpression && vectorExpression.Parameter.Equals(Parameter);
        }
    }
}
