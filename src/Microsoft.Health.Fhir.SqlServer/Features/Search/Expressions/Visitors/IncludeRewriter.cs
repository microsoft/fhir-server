// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
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

        private static readonly TableExpressionComparer Comparer = new TableExpressionComparer();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 1 || expression.TableExpressions.All(e => e.Kind != TableExpressionKind.Include))
            {
                return expression;
            }

            bool containsInclude = expression.TableExpressions.Any(e => e.SearchParameterQueryGenerator is IncludeQueryGenerator);

            if (!containsInclude)
            {
                return new SqlRootExpression(expression.TableExpressions, expression.DenormalizedExpressions);
            }

            // Order expressions so that simple search parameters appear first and include parameters appear after
            // In addition, sort that _include:iterate and _revinclude:iterate parameters are ordered so that the
            // included results they reference appears before
            List<TableExpression> reorderedExpressions = expression.TableExpressions.OrderBy(t => t, Comparer).ToList();

            // We are adding an extra CTE after each include cte, so we traverse the ordered
            // list from the end and add a limit expression after each include expression
            for (var i = reorderedExpressions.Count - 1; i >= 0; i--)
            {
                switch (reorderedExpressions[i].SearchParameterQueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        reorderedExpressions.Insert(i + 1, IncludeLimitExpression);
                        break;
                    default:
                        break;
                }
            }

            reorderedExpressions.Add(IncludeUnionAllExpression);
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
                        return 1;
                    }

                    // Both expressions are Include expressions
                    var xInclude = (IncludeExpression)x.NormalizedPredicate;
                    var yInclude = (IncludeExpression)y.NormalizedPredicate;

                    if (!xInclude.Iterate && yInclude.Iterate)
                    {
                        return -1;
                    }

                    if (xInclude.Iterate && !yInclude.Iterate)
                    {
                        return 1;
                    }

                    // Both expressions are Include:iterate expressions, order so that _include:iterate source type will appear after relevant include target
                    var xTargetTypes = !string.IsNullOrEmpty(xInclude.TargetResourceType) ? new List<string>() { xInclude.TargetResourceType } : xInclude.ReferenceSearchParameter.TargetResourceTypes;
                    var yTargetTypes = !string.IsNullOrEmpty(yInclude.TargetResourceType) ? new List<string>() { yInclude.TargetResourceType } : yInclude.ReferenceSearchParameter.TargetResourceTypes;

                    // Both are RevInclude Iterate expressions
                    if (xInclude.Reversed && yInclude.Reversed && yTargetTypes.Contains(xInclude.ResourceType))
                    {
                        return -1;
                    }

                    // Both are Include Iterate expressions
                    if (!xInclude.Reversed && !yInclude.Reversed && xTargetTypes.Contains(yInclude.ResourceType))
                    {
                        return -1;
                    }

                    // One expression is reveresed and the other one isn't, their resource type should match
                    if (xInclude.ResourceType == yInclude.ResourceType)
                    {
                        return xInclude.Reversed ? -1 : 1;
                    }

                    return 0;
                }
                else if (!(y.SearchParameterQueryGenerator is IncludeQueryGenerator))
                {
                    return 0;
                }

                return -1;
            }
        }
    }
}
