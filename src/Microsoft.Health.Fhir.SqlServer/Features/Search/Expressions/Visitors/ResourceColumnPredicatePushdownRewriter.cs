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
            if (expression.SearchParamTableExpressions.Count == 0 || expression.ResourceTableExpressions.Count == 0 ||
                expression.SearchParamTableExpressions.All(e => e.Kind == SearchParamTableExpressionKind.Include))
            {
                // if only Include expressions, the case is handled in IncludeMatchSeedRewriter
                return expression;
            }

            Expression extractedCommonResourceExpressions = null;
            bool containsResourceExpressionFoundOnlyOnResourceTable = false;

            for (int i = 0; i < expression.ResourceTableExpressions.Count; i++)
            {
                SearchParameterExpressionBase currentExpression = expression.ResourceTableExpressions[i];

                if (currentExpression is SearchParameterExpression searchParameterExpression)
                {
                    if (searchParameterExpression.Parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.SearchParamTable))
                    {
                        extractedCommonResourceExpressions = extractedCommonResourceExpressions == null ? currentExpression : Expression.And(extractedCommonResourceExpressions, currentExpression);
                    }
                    else
                    {
                        containsResourceExpressionFoundOnlyOnResourceTable = true;
                    }
                }
            }

            var newTableExpressions = new List<SearchParamTableExpression>(expression.SearchParamTableExpressions.Count);

            if (containsResourceExpressionFoundOnlyOnResourceTable)
            {
                // There is a predicate over _id, which is on the Resource table but not on the search parameter tables.
                // So the first table expression should be an "All" expression, where we restrict the resultset to resources with that ID.
                newTableExpressions.Add(new SearchParamTableExpression(null, Expression.And(expression.ResourceTableExpressions), SearchParamTableExpressionKind.All));
            }

            foreach (var tableExpression in expression.SearchParamTableExpressions)
            {
                if (tableExpression.Kind == SearchParamTableExpressionKind.Include ||
                    (tableExpression.Kind == SearchParamTableExpressionKind.Normal && tableExpression.ChainLevel > 0) ||
                    (tableExpression.Kind == SearchParamTableExpressionKind.Chain && tableExpression.ChainLevel > 1))
                {
                    // these predicates do not apply to referenced resources

                    newTableExpressions.Add(tableExpression);
                }
                else if (tableExpression.Kind == SearchParamTableExpressionKind.Chain)
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

                    newTableExpressions.Add(new SearchParamTableExpression(tableExpression.QueryGenerator, newChainLinkExpression, tableExpression.Kind, chainLevel: tableExpression.ChainLevel));
                }
                else
                {
                    Expression predicate = tableExpression.Predicate == null
                        ? extractedCommonResourceExpressions
                        : Expression.And(tableExpression.Predicate, extractedCommonResourceExpressions);

                    newTableExpressions.Add(new SearchParamTableExpression(tableExpression.QueryGenerator, predicate, tableExpression.Kind, tableExpression.ChainLevel));
                }
            }

            return new SqlRootExpression(newTableExpressions, Array.Empty<SearchParameterExpressionBase>());
        }
    }
}
