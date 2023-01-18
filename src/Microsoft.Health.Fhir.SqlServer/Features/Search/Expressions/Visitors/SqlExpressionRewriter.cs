// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal abstract class SqlExpressionRewriter<TContext> : ExpressionRewriter<TContext>, ISqlExpressionVisitor<TContext, Expression>
    {
        public virtual Expression VisitSqlRoot(SqlRootExpression expression, TContext context)
        {
            IReadOnlyList<SearchParameterExpressionBase> visitedResourceExpressions = VisitArray(expression.ResourceTableExpressions, context);
            IReadOnlyList<SearchParamTableExpression> visitedTableExpressions = VisitArray(expression.SearchParamTableExpressions, context);

            if (ReferenceEquals(visitedTableExpressions, expression.SearchParamTableExpressions) &&
                ReferenceEquals(visitedResourceExpressions, expression.ResourceTableExpressions))
            {
                return expression;
            }

            return new SqlRootExpression(visitedTableExpressions, visitedResourceExpressions);
        }

        public virtual Expression VisitTable(SearchParamTableExpression searchParamTableExpression, TContext context)
        {
            Expression rewrittenPredicate = searchParamTableExpression.Predicate?.AcceptVisitor(this, context);

            if (ReferenceEquals(rewrittenPredicate, searchParamTableExpression.Predicate))
            {
                return searchParamTableExpression;
            }

            int chainLevel = 0;
            if (((rewrittenPredicate as SearchParameterExpression)?.Expression as MultiaryExpression)?.MultiaryOperation == MultiaryOperator.Or)
            {
                chainLevel = 1;
            }

            return new SearchParamTableExpression(searchParamTableExpression.QueryGenerator, rewrittenPredicate, searchParamTableExpression.Kind, chainLevel);
        }

        public virtual Expression VisitSqlChainLink(SqlChainLinkExpression sqlChainLinkExpression, TContext context)
        {
            Expression visitedExpressionOnSource = sqlChainLinkExpression.ExpressionOnSource?.AcceptVisitor(this, context);
            Expression visitedExpressionOnTarget = sqlChainLinkExpression.ExpressionOnTarget?.AcceptVisitor(this, context);

            if (ReferenceEquals(visitedExpressionOnSource, sqlChainLinkExpression.ExpressionOnSource) &&
                ReferenceEquals(visitedExpressionOnTarget, sqlChainLinkExpression.ExpressionOnTarget))
            {
                return sqlChainLinkExpression;
            }

            return new SqlChainLinkExpression(
                sqlChainLinkExpression.ResourceTypes,
                sqlChainLinkExpression.ReferenceSearchParameter,
                sqlChainLinkExpression.TargetResourceTypes,
                sqlChainLinkExpression.Reversed,
                visitedExpressionOnSource,
                visitedExpressionOnTarget);
        }
    }
}
