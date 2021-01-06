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
    internal class ResourceColumnPredicatePushdownRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly ResourceColumnPredicatePushdownRewriter Instance = new ResourceColumnPredicatePushdownRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0 || expression.ResourceExpressions.Count == 0 ||
                expression.TableExpressions.All(e => e.Kind == TableExpressionKind.Include))
            {
                // if only Include expressions, the case is handled in IncludeMatchSeedRewriter
                return expression;
            }

            Expression extractedCommonResourceExpressions = null;
            bool containsResourceExpressionFoundOnlyOnResourceTable = false;

            for (int i = 0; i < expression.ResourceExpressions.Count; i++)
            {
                SearchParameterExpressionBase currentExpression = expression.ResourceExpressions[i];

                if (currentExpression is SearchParameterExpression searchParameterExpression)
                {
                    if (searchParameterExpression.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.SearchParamTable))
                    {
                        extractedCommonResourceExpressions = extractedCommonResourceExpressions == null ? (Expression)currentExpression : Expression.And(extractedCommonResourceExpressions, currentExpression);
                    }
                    else
                    {
                        containsResourceExpressionFoundOnlyOnResourceTable = true;
                    }
                }
            }

            var newTableExpressions = new List<TableExpression>(expression.TableExpressions.Count);

            if (containsResourceExpressionFoundOnlyOnResourceTable)
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
                    var sqlChainLinkExpression = (SqlChainLinkExpression)tableExpression.Predicate;

                    Debug.Assert(sqlChainLinkExpression.ExpressionOnSource == null);

                    var newChainLinkExpression = new SqlChainLinkExpression(
                        sqlChainLinkExpression.ResourceTypes,
                        sqlChainLinkExpression.ReferenceSearchParameter,
                        sqlChainLinkExpression.TargetResourceTypes,
                        sqlChainLinkExpression.Reversed,
                        extractedCommonResourceExpressions,
                        sqlChainLinkExpression.ExpressionOnTarget);

                    newTableExpressions.Add(new TableExpression(tableExpression.QueryGenerator, newChainLinkExpression, tableExpression.Kind, chainLevel: tableExpression.ChainLevel));
                }
                else
                {
                    Expression predicate = tableExpression.Predicate == null
                        ? extractedCommonResourceExpressions
                        : Expression.And(tableExpression.Predicate, extractedCommonResourceExpressions);

                    newTableExpressions.Add(new TableExpression(tableExpression.QueryGenerator, predicate, tableExpression.Kind, tableExpression.ChainLevel));
                }
            }

            return new SqlRootExpression(newTableExpressions, Array.Empty<SearchParameterExpressionBase>());
        }
    }
}
