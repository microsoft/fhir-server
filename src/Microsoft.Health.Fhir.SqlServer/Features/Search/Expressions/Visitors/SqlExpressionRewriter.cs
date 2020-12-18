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
            IReadOnlyList<SearchParameterExpressionBase> denormalizedPredicates = VisitArray(expression.ResourceExpressions, context);
            IReadOnlyList<TableExpression> normalizedPredicates = VisitArray(expression.TableExpressions, context);

            if (ReferenceEquals(normalizedPredicates, expression.TableExpressions) &&
                ReferenceEquals(denormalizedPredicates, expression.ResourceExpressions))
            {
                return expression;
            }

            return new SqlRootExpression(normalizedPredicates, denormalizedPredicates);
        }

        public virtual Expression VisitTable(TableExpression tableExpression, TContext context)
        {
            Expression denormalizedPredicate = tableExpression.DenormalizedPredicate?.AcceptVisitor(this, context);
            Expression normalizedPredicate = tableExpression.NormalizedPredicate?.AcceptVisitor(this, context);

            if (ReferenceEquals(denormalizedPredicate, tableExpression.DenormalizedPredicate) &&
                ReferenceEquals(normalizedPredicate, tableExpression.NormalizedPredicate))
            {
                return tableExpression;
            }

            return new TableExpression(tableExpression.SearchParameterQueryGenerator, normalizedPredicate, denormalizedPredicate, tableExpression.Kind);
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
