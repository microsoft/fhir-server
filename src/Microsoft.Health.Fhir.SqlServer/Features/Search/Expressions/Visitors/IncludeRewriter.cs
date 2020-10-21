// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewriter used to put the include expressions at the end of the list of table expressions.
    /// </summary>
    internal class IncludeRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly IncludeRewriter Instance = new IncludeRewriter();

        private static readonly TableExpression IncludeUnionAllExpression = new TableExpression(null, null, null, TableExpressionKind.IncludeUnionAll);
        private static readonly TableExpression IncludeLimitExpression = new TableExpression(null, null, null, TableExpressionKind.IncludeLimit);

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 1 || expression.TableExpressions.All(e => e.Kind != TableExpressionKind.Include))
            {
                return expression;
            }

            // TableExpressions contains at least one Include expression
            var nonIncludeExpressions = expression.TableExpressions.Where(e => e.Kind != TableExpressionKind.Include).ToList();
            var includeExpressions = expression.TableExpressions.Where(e => e.Kind == TableExpressionKind.Include).ToList();

            // Sort include expressions if there is an include iterate expression
            // Order so that include iterate expression appear after the expressions they select from
            IEnumerable<TableExpression> sortedIncludeExpressions = includeExpressions;

            if (includeExpressions.Any(e => ((IncludeExpression)e.NormalizedPredicate).Iterate))
            {
                IEnumerable<TableExpression> nonIterateExpressions = includeExpressions.Where(e => !((IncludeExpression)e.NormalizedPredicate).Iterate);
                List<TableExpression> iterateExpressions = includeExpressions.Where(e => ((IncludeExpression)e.NormalizedPredicate).Iterate).ToList();
                sortedIncludeExpressions = nonIterateExpressions.Concat(SortIncludeIterateExpressions(iterateExpressions));
            }

            // Add sorted include expressions after all other expressions
            var reorderedExpressions = nonIncludeExpressions.Concat(sortedIncludeExpressions).ToList();

            // We are adding an extra CTE after each include cte, so we traverse the ordered
            // list from the end and add a limit expression after each include expression
            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                switch (reorderedExpressions[i].SearchParameterQueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        reorderedExpressions.Insert(i + 1, IncludeLimitExpression);
                        break;
                }
            }

            reorderedExpressions.Add(IncludeUnionAllExpression);
            return new SqlRootExpression(reorderedExpressions, expression.DenormalizedExpressions);
        }

        private static IList<TableExpression> SortIncludeIterateExpressions(IList<TableExpression> expressions)
        {
            if (expressions.Count == 1)
            {
                return expressions;
            }

            ILookup<string, TableExpression> expressionsThatProduceType = expressions.
                SelectMany(expression => ((IncludeExpression)expression.NormalizedPredicate).Produces.Select(producedType => (producedType, expression)))
                .ToLookup(p => p.producedType, p => p.expression, StringComparer.Ordinal);

            var visited = new Dictionary<TableExpression, bool>();
            var sorted = new List<TableExpression>();

            foreach (TableExpression tableExpression in expressions)
            {
                Dfs(tableExpression);
            }

            return sorted;

            void Dfs(TableExpression e)
            {
                if (visited.TryGetValue(e, out var completed))
                {
                    if (!completed)
                    {
                        throw new BadRequestException("cycle...");
                    }

                    return;
                }

                // mark visiting
                visited.Add(e, false);

                foreach (string requiredType in ((IncludeExpression)e.NormalizedPredicate).Requires)
                {
                    foreach (TableExpression producingExpression in expressionsThatProduceType[requiredType])
                    {
                        Dfs(producingExpression);
                    }
                }

                // mark visited
                visited[e] = true;
                sorted.Add(e);
            }
        }
    }
}
