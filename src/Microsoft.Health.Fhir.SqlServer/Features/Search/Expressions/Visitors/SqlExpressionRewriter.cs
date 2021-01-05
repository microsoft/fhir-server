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
            IReadOnlyList<SearchParameterExpressionBase> visitedResourceExpressions = VisitArray(expression.ResourceExpressions, context);
            IReadOnlyList<TableExpression> visitedTableExpressions = VisitArray(expression.TableExpressions, context);

            if (ReferenceEquals(visitedTableExpressions, expression.TableExpressions) &&
                ReferenceEquals(visitedResourceExpressions, expression.ResourceExpressions))
            {
                return expression;
            }

            return new SqlRootExpression(visitedTableExpressions, visitedResourceExpressions);
        }

        public virtual Expression VisitTable(TableExpression tableExpression, TContext context)
        {
            Expression rewrittenPredicate = tableExpression.Predicate?.AcceptVisitor(this, context);

            if (ReferenceEquals(rewrittenPredicate, tableExpression.Predicate))
            {
                return tableExpression;
            }

            return new TableExpression(tableExpression.QueryGenerator, rewrittenPredicate, tableExpression.Kind);
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
                sqlChainLinkExpression.ResourceType,
                sqlChainLinkExpression.ReferenceSearchParameter,
                sqlChainLinkExpression.TargetResourceType,
                sqlChainLinkExpression.Reversed,
                visitedExpressionOnSource,
                visitedExpressionOnTarget);
        }
    }
}
