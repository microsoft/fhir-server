// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal sealed class RemoveVectorSearchRewriter : ExpressionRewriterWithInitialContext<object>
    {
        public static readonly RemoveVectorSearchRewriter Instance = new RemoveVectorSearchRewriter();

        public override Expression VisitVectorSearch(VectorSearchExpression expression, object context)
        {
            return null;
        }

        public override Expression VisitChained(ChainedExpression expression, object context)
        {
            Expression rewrittenExpression = expression.Expression.AcceptVisitor(this, context);
            if (ReferenceEquals(rewrittenExpression, expression.Expression))
            {
                return expression;
            }

            if (rewrittenExpression == null)
            {
                return null;
            }

            return new ChainedExpression(
                expression.ResourceTypes,
                expression.ReferenceSearchParameter,
                expression.TargetResourceTypes,
                expression.Reversed,
                rewrittenExpression);
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            List<Expression> rewrittenExpressions = null;

            for (int index = 0; index < expression.Expressions.Count; index++)
            {
                Expression originalExpression = expression.Expressions[index];
                Expression rewrittenExpression = originalExpression.AcceptVisitor(this, context);

                if (!ReferenceEquals(originalExpression, rewrittenExpression))
                {
                    EnsureAllocatedAndPopulated(ref rewrittenExpressions, expression.Expressions, index);
                }

                if (rewrittenExpression != null)
                {
                    rewrittenExpressions?.Add(rewrittenExpression);
                }
            }

            return rewrittenExpressions switch
            {
                null => expression,
                { Count: 0 } => null,
                { Count: 1 } => rewrittenExpressions[0],
                _ => new MultiaryExpression(expression.MultiaryOperation, rewrittenExpressions),
            };
        }
    }
}
