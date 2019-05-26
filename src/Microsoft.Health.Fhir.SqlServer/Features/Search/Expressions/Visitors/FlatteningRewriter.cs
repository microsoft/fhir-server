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
    internal class FlatteningRewriter : ExpressionRewriterWithDefaultInitialContext<object>
    {
        public static readonly FlatteningRewriter Instance = new FlatteningRewriter();

        private FlatteningRewriter()
        {
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            var visitedExpression = (MultiaryExpression)base.VisitMultiary(expression, context);
            if (visitedExpression.Expressions.Count == 1)
            {
                return visitedExpression.Expressions[0];
            }

            List<Expression> newExpressions = null;

            for (var i = 0; i < visitedExpression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];
                if (childExpression is MultiaryExpression childMultiary && childMultiary.MultiaryOperation == visitedExpression.MultiaryOperation)
                {
                    if (newExpressions == null)
                    {
                        newExpressions = new List<Expression>();
                        for (var j = 0; j < i; j++)
                        {
                            newExpressions.Add(expression.Expressions[i]);
                        }
                    }

                    newExpressions.AddRange(childMultiary.Expressions);
                }
                else
                {
                    newExpressions?.Add(childExpression);
                }
            }

            if (newExpressions == null)
            {
                return visitedExpression;
            }

            return new MultiaryExpression(visitedExpression.MultiaryOperation, newExpressions);
        }
    }
}
