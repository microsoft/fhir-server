// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a multiary expression where <see cref="Expressions"/> are grouped by <see cref="MultiaryOperation"/>.
    /// </summary>
    public class MultiaryExpression : Expression, IExpressionsContainer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MultiaryExpression"/> class.
        /// </summary>
        /// <param name="multiaryOperation">The multiary operator type.</param>
        /// <param name="expressions">The expressions.</param>
        public MultiaryExpression(MultiaryOperator multiaryOperation, IReadOnlyList<Expression> expressions)
        {
            EnsureArg.IsNotNull(expressions, nameof(expressions));
            EnsureArg.IsTrue(expressions.Any(), nameof(expressions));
            EnsureArg.IsTrue(expressions.All(o => o != null), nameof(expressions));

            MultiaryOperation = multiaryOperation;
            Expressions = expressions;
        }

        /// <summary>
        /// Gets the multiary operator type.
        /// </summary>
        public MultiaryOperator MultiaryOperation { get; }

        /// <summary>
        /// Gets the expressions.
        /// </summary>
        public IReadOnlyList<Expression> Expressions { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitMultiary(this, context);
        }

        public override string ToString()
        {
            return $"({MultiaryOperation} {string.Join(' ', Expressions)})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(MultiaryExpression));
            hashCode.Add(MultiaryOperation);
            foreach (Expression expression in Expressions)
            {
                expression.AddValueInsensitiveHashCode(ref hashCode);
            }
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            if (other is not MultiaryExpression multiary ||
                multiary.MultiaryOperation != MultiaryOperation ||
                multiary.Expressions.Count != Expressions.Count)
            {
                return false;
            }

            for (var i = 0; i < Expressions.Count; i++)
            {
                if (!multiary.Expressions[i].ValueInsensitiveEquals(Expressions[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
