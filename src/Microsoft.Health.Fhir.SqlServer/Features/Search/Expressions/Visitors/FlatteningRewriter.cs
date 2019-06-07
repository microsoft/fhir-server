// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Flattens multiary expressions when possible.
    /// (And (And a b) (And c d)) -> (And a b c d)
    /// (And a) -> a
    /// </summary>
    internal class FlatteningRewriter : ExpressionRewriterWithInitialContext<object>
    {
        public static readonly FlatteningRewriter Instance = new FlatteningRewriter();

        private FlatteningRewriter()
        {
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            expression = (MultiaryExpression)base.VisitMultiary(expression, context);
            if (expression.Expressions.Count == 1)
            {
                return expression.Expressions[0];
            }

            List<Expression> newExpressions = null;

            for (var i = 0; i < expression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];
                if (childExpression is MultiaryExpression childMultiary && childMultiary.MultiaryOperation == expression.MultiaryOperation)
                {
                    EnsureAllocatedAndPopulated(ref newExpressions, expression.Expressions, i);
                    newExpressions.AddRange(childMultiary.Expressions);
                }
                else
                {
                    newExpressions?.Add(childExpression);
                }
            }

            if (newExpressions == null)
            {
                return expression;
            }

            return new MultiaryExpression(expression.MultiaryOperation, newExpressions);
        }
    }
}
