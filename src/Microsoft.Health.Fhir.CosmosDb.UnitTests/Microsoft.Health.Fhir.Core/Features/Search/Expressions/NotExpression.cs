// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a not expression where <see cref="Expression"/> is negated.
    /// </summary>
    public class NotExpression : Expression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotExpression"/> class.
        /// </summary>
        /// <param name="expression">The expression.</param>
        public NotExpression(Expression expression)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            Expression = expression;
        }

        /// <summary>
        /// Gets the expression.
        /// </summary>
        public Expression Expression { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitNotExpression(this, context);
        }

        public override string ToString()
        {
            return $"(Not {Expression})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(NotExpression));
            Expression.AddValueInsensitiveHashCode(ref hashCode);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is NotExpression notExpression && notExpression.ValueInsensitiveEquals(Expression);
        }
    }
}
