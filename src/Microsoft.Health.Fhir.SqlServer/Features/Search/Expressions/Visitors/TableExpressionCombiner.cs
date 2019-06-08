// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Combines <see cref="TableExpression"/>s that are over the same search parameter.
    /// For example, probability=gt0.8&amp;probability=lt0.9 will end up as separate table expressions,
    /// but the query will be more efficient if they are combined.
    /// </summary>
    internal class TableExpressionCombiner : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly TableExpressionCombiner Instance = new TableExpressionCombiner();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            // This rewriter is a little heavier, so we bail out early if we can.
            switch (expression.TableExpressions.Count)
            {
                case 0:
                case 1:
                case 2 when expression.TableExpressions[0].SearchParameterQueryGenerator != expression.TableExpressions[1].SearchParameterQueryGenerator:
                case 3 when expression.TableExpressions[0].SearchParameterQueryGenerator != expression.TableExpressions[1].SearchParameterQueryGenerator &&
                            expression.TableExpressions[1].SearchParameterQueryGenerator != expression.TableExpressions[2].SearchParameterQueryGenerator &&
                            expression.TableExpressions[0].SearchParameterQueryGenerator != expression.TableExpressions[2].SearchParameterQueryGenerator:
                    return expression;
            }

            var newTableExpressions = expression.TableExpressions
                .GroupBy(t => (searchParameter: (t.NormalizedPredicate as SearchParameterExpression)?.Parameter, queryGenerator: t.SearchParameterQueryGenerator))
                .SelectMany(g =>
                {
                    if (g.Key.searchParameter == null)
                    {
                        return (IEnumerable<TableExpression>)g;
                    }

                    var childExpressions = new List<Expression>();
                    foreach (TableExpression tableExpression in g)
                    {
                        Debug.Assert(tableExpression.Kind == TableExpressionKind.Normal, "Kind should not be set yet");
                        Debug.Assert(tableExpression.DenormalizedPredicate == null, "Denormalized predicates should not be set yet");

                        var searchParameterExpression = (SearchParameterExpression)tableExpression.NormalizedPredicate;

                        Expression innerExpression = searchParameterExpression.Expression;

                        if (innerExpression is MultiaryExpression multiary && multiary.MultiaryOperation == MultiaryOperator.And)
                        {
                            childExpressions.AddRange(multiary.Expressions);
                        }
                        else
                        {
                            childExpressions.Add(innerExpression);
                        }
                    }

                    return new[] { new TableExpression(g.Key.queryGenerator, Expression.SearchParameter(g.Key.searchParameter, Expression.And(childExpressions)), null, TableExpressionKind.Normal) };
                }).ToList();

            return new SqlRootExpression(newTableExpressions, expression.DenormalizedExpressions);
        }
    }
}
