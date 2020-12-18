// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Promotes predicates applied directly in on the Resource table to the search parameter tables.
    /// These are predicates on the ResourceSurrogateId and ResourceType columns. The idea is to make these
    /// queries as selective as possible.
    /// </summary>
    internal class DenormalizedPredicateRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly DenormalizedPredicateRewriter Instance = new DenormalizedPredicateRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0 || expression.ResourceExpressions.Count == 0 ||
                expression.TableExpressions.All(e => e.Kind == TableExpressionKind.Include))
            {
                // if only Include expressions, the case is handled in IncludeDenormalizedRewriter
                return expression;
            }

            Expression extractedDenormalizedExpression = null;
            List<Expression> newDenormalizedPredicates = null;
            bool containsDenormalizedExpressionNotOnSearchParameterTables = false;

            for (int i = 0; i < expression.ResourceExpressions.Count; i++)
            {
                SearchParameterExpressionBase currentExpression = expression.ResourceExpressions[i];

                if (currentExpression is SearchParameterExpression searchParameterExpression)
                {
                    if (searchParameterExpression.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.SearchParamTable))
                    {
                        extractedDenormalizedExpression = extractedDenormalizedExpression == null ? (Expression)currentExpression : Expression.And(extractedDenormalizedExpression, currentExpression);
                        EnsureAllocatedAndPopulated(ref newDenormalizedPredicates, expression.ResourceExpressions, i);
                    }
                    else
                    {
                        containsDenormalizedExpressionNotOnSearchParameterTables = true;
                        newDenormalizedPredicates?.Add(currentExpression);
                    }
                }
            }

            var newTableExpressions = new List<TableExpression>(expression.TableExpressions.Count);

            if (containsDenormalizedExpressionNotOnSearchParameterTables)
            {
                // There is a predicate over _id, which is on the Resource table but not on the search parameter tables.
                // So the first table expression should be an "All" expression, where we restrict the resultset to resources with that ID.
                newTableExpressions.Add(new TableExpression(null, Expression.And(expression.ResourceExpressions), TableExpressionKind.All));
            }

            foreach (var tableExpression in expression.TableExpressions)
            {
                if (tableExpression.Kind == TableExpressionKind.Include ||
                    (tableExpression.Kind == TableExpressionKind.Normal && tableExpression.ChainLevel > 0) ||
                    (tableExpression.Kind == TableExpressionKind.Chain && tableExpression.ChainLevel > 1))
                {
                    // these predicates do not apply to referenced resources

                    newTableExpressions.Add(tableExpression);
                }
                else if (tableExpression.Kind == TableExpressionKind.Chain)
                {
                    var sqlChainLinkExpression = (SqlChainLinkExpression)tableExpression.NormalizedPredicate;

                    Debug.Assert(sqlChainLinkExpression.ExpressionOnSource == null);

                    var newChainLinkExpression = new SqlChainLinkExpression(
                        sqlChainLinkExpression.ResourceType,
                        sqlChainLinkExpression.ReferenceSearchParameter,
                        sqlChainLinkExpression.TargetResourceType,
                        sqlChainLinkExpression.Reversed,
                        extractedDenormalizedExpression,
                        sqlChainLinkExpression.ExpressionOnTarget);

                    newTableExpressions.Add(new TableExpression(tableExpression.SearchParameterQueryGenerator, newChainLinkExpression, tableExpression.Kind, chainLevel: tableExpression.ChainLevel));
                }
                else
                {
                    Expression newDenormalizedPredicate = tableExpression.NormalizedPredicate == null
                        ? extractedDenormalizedExpression
                        : Expression.And(tableExpression.NormalizedPredicate, extractedDenormalizedExpression);

                    newTableExpressions.Add(new TableExpression(tableExpression.SearchParameterQueryGenerator, newDenormalizedPredicate, tableExpression.Kind, tableExpression.ChainLevel));
                }
            }

            return new SqlRootExpression(newTableExpressions, Array.Empty<SearchParameterExpressionBase>());
        }
    }
}
