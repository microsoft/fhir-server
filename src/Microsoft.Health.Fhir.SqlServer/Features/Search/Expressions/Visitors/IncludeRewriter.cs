// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
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

            List<TableExpression> reorderedExpressions = expression.TableExpressions.OrderByDescending(t => t, new TableExpressionComparer()).ToList();

            bool containsInclude = expression.TableExpressions.AsEnumerable().Where(e => e.SearchParameterQueryGenerator is IncludeQueryGenerator).Any();

            // We are adding an extra CTE after each include cte (except recursive :iterate), so we traverse the ordered
            // list from the end and add a limit expression after each include expression
            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                switch (reorderedExpressions[i].SearchParameterQueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        var includeExpression = (IncludeExpression)reorderedExpressions[i].NormalizedPredicate;
                        if (!includeExpression.Recursive)
                        {
                            reorderedExpressions.Insert(i + 1, IncludeLimitExpression);
                        }

                        break;
                    default:
                        break;
                }
            }

            if (containsInclude)
            {
                reorderedExpressions.Add(IncludeUnionAllExpression);
            }

            return new SqlRootExpression(reorderedExpressions, expression.DenormalizedExpressions);
        }

        private class TableExpressionComparer : IComparer<TableExpression>
        {
            public int Compare(TableExpression x, TableExpression y)
            {
                if (x.SearchParameterQueryGenerator is IncludeQueryGenerator)
                {
                    if (!(y.SearchParameterQueryGenerator is IncludeQueryGenerator))
                    {
                        return -1;
                    }

                    // Both expressions are Include expressions
                    var xInclude = (IncludeExpression)(IncludeExpression)x.NormalizedPredicate;
                    var yInclude = (IncludeExpression)(IncludeExpression)y.NormalizedPredicate;

                    if (!xInclude.Iterate && yInclude.Iterate)
                    {
                        return 1;
                    }

                    if (xInclude.Iterate && !yInclude.Iterate)
                    {
                        return -1;
                    }

                    // Both expressions are Include:iterate expressions, order so that _include:iterate source type will appear after relevant include target
                    var xTargetTypes = !string.IsNullOrEmpty(xInclude.TargetResourceType) ? new List<string>() { xInclude.TargetResourceType } : xInclude.ReferenceSearchParameter.TargetResourceTypes;

                    if (xTargetTypes.Contains(yInclude.ResourceType))
                    {
                        return 1;
                    }

                    return 0;
                }
                else if (!(y.SearchParameterQueryGenerator is IncludeQueryGenerator))
                {
                    return 0;
                }

                return 1;
            }
        }
    }
}
