// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Promotes predicates applied directly in on the Resource table to the search parameter tables.
    /// These are predicates on the ResourceSurrogateId and ResourceType columns. The idea is to make these
    /// queries as selective as possible.
    /// </summary>
    internal class DenormalizedPredicateRewriter : ExpressionRewriterWithInitialContext<object>, ISqlExpressionVisitor<object, Expression>
    {
        public static readonly DenormalizedPredicateRewriter Instance = new DenormalizedPredicateRewriter();

        public Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0 || expression.DenormalizedExpressions.Count == 0 || expression.TableExpressions.All(t => t.Kind == TableExpressionKind.Chain))
            {
                return expression;
            }

            Expression extractedDenormalizedExpression = null;
            List<Expression> newDenormalizedPredicates = null;

            for (int i = 0; i < expression.DenormalizedExpressions.Count; i++)
            {
                Expression currentExpression = expression.DenormalizedExpressions[i];

                if (currentExpression is SearchParameterExpression searchParameterExpression)
                {
                    switch (searchParameterExpression.Parameter.Name)
                    {
                        case SqlSearchParameters.ResourceSurrogateIdParameterName:
                        case SearchParameterNames.ResourceType:
                            extractedDenormalizedExpression = extractedDenormalizedExpression == null ? currentExpression : Expression.And(extractedDenormalizedExpression, currentExpression);
                            EnsureAllocatedAndPopulated(ref newDenormalizedPredicates, expression.DenormalizedExpressions, i);

                            break;
                        default:
                            newDenormalizedPredicates?.Add(expression);
                            break;
                    }
                }
            }

            if (extractedDenormalizedExpression == null)
            {
                return expression;
            }

            var newTableExpressions = new List<TableExpression>(expression.TableExpressions.Count);
            foreach (var tableExpression in expression.TableExpressions)
            {
                if (tableExpression.Kind == TableExpressionKind.Chain)
                {
                    newTableExpressions.Add(tableExpression);
                }
                else
                {
                    Expression newDenormalizedPredicate = tableExpression.DenormalizedPredicate == null
                        ? extractedDenormalizedExpression
                        : Expression.And(tableExpression.DenormalizedPredicate, extractedDenormalizedExpression);

                    newTableExpressions.Add(new TableExpression(tableExpression.SearchParameterQueryGenerator, tableExpression.NormalizedPredicate, newDenormalizedPredicate, tableExpression.Kind));
                }
            }

            return new SqlRootExpression(newTableExpressions, newDenormalizedPredicates);
        }

        public Expression VisitTable(TableExpression tableExpression, object context)
        {
            throw new InvalidOperationException();
        }
    }
}
