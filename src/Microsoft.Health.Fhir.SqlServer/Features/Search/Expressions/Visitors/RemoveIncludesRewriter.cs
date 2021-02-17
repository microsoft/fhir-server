// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// A rewriter that removes <see cref="IncludeExpression">s from an expression tree.
    /// </summary>
    internal class RemoveIncludesRewriter : ExpressionRewriterWithInitialContext<object>
    {
        public static readonly RemoveIncludesRewriter Instance = new RemoveIncludesRewriter();

        public override Expression VisitInclude(IncludeExpression expression, object context)
        {
            return null;
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            List<Expression> newChildExpressions = null;
            for (int i = 0; i < expression.Expressions.Count; i++)
            {
                Expression childExpression = expression.Expressions[i];
                if (childExpression is IncludeExpression)
                {
                    if (i == 0 && expression.Expressions.All(e => e is IncludeExpression))
                    {
                        return null;
                    }

                    EnsureAllocatedAndPopulated(ref newChildExpressions, expression.Expressions, i);
                }
                else
                {
                    newChildExpressions?.Add(childExpression);
                }
            }

            return newChildExpressions switch
            {
                null => expression,
                { Count: 0 } => null,
                { Count: 1 } => newChildExpressions[0],
                _ => new MultiaryExpression(expression.MultiaryOperation, newChildExpressions),
            };
        }
    }
}
