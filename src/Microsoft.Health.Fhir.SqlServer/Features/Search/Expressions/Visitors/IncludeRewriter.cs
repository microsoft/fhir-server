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

            // TableExpressions contains at least one Include expression
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
                if (x.Kind == TableExpressionKind.Include)
                {
                    // x is an include expression and y isn't => x > y
                    if (y.Kind != TableExpressionKind.Include)
                    {
                        return 1;
                    }

                    // Both expressions are Include expressions
                    var xInclude = (IncludeExpression)x.NormalizedPredicate;
                    var yInclude = (IncludeExpression)y.NormalizedPredicate;

                    // If both are not iterate expressions then the order doesn't matter
                    if (!xInclude.Iterate && !yInclude.Iterate)
                    {
                        return 0;
                    }

                    // At least one expression is iterative
                    // Iterative include expressions should come after the non iterative ones
                    if (!xInclude.Iterate && yInclude.Iterate)
                    {
                        return -1;
                    }

                    if (xInclude.Iterate && !yInclude.Iterate)
                    {
                        return 1;
                    }

                    // Both expressions are :iterate expressions => Order so that _include:iterate source type will appear after relevant include target
                    // and _revinclude:iterate target type appears after it's already been included

                    var xTargetTypes = xInclude.ReferenceSearchParameter?.TargetResourceTypes;
                    var yTargetTypes = yInclude.ReferenceSearchParameter?.TargetResourceTypes;

                    // Both are _revinclude:iterate or both are _include:iterate, order so that one's target resource type should appear after the other's source resource types if types are equal
                    if (xInclude.Reversed == yInclude.Reversed)
                    {
                        // x's target type matches y's source type => x > y
                        if ((xInclude.TargetResourceType != null && xInclude.TargetResourceType == yInclude.SourceResourceType)
                            || (xInclude.TargetResourceType == null && xTargetTypes != null && xTargetTypes.Contains(yInclude.SourceResourceType)))
                        {
                            return xInclude.Reversed ? 1 : -1;
                        }

                        // y's target type matches x's source type => x < y
                        if ((yInclude.TargetResourceType != null && yInclude.TargetResourceType == xInclude.SourceResourceType)
                            || (yInclude.TargetResourceType == null && yTargetTypes != null && yTargetTypes.Contains(xInclude.SourceResourceType)))
                        {
                            return xInclude.Reversed ? -1 : 1;
                        }

                        return 0; // Keep the same order
                    }

                    // x is _include:iterate and y is _revinclude:iterate
                    else if (!xInclude.Reversed && yInclude.Reversed)
                    {
                        // x's specified target type matches y's target type => x < y
                        if ((xInclude.TargetResourceType != null && yInclude.TargetResourceType != null && xInclude.TargetResourceType == yInclude.TargetResourceType)
                            || (xInclude.TargetResourceType != null && yTargetTypes != null && yTargetTypes.Contains(xInclude.TargetResourceType)))
                        {
                            return -1;
                        }

                        // one of x's target types matches y's source type => x < y
                        if (xInclude.TargetResourceType == null && xTargetTypes != null && xTargetTypes.Contains(yInclude.SourceResourceType))
                        {
                            return -1;
                        }

                        return 1;
                    }

                    // x is _revinclude:iterate and y is _include:iterate
                    else if (xInclude.Reversed && !yInclude.Reversed)
                    {
                        // y's specified target type matches x's target type => x > y
                        if ((yInclude.TargetResourceType != null && xInclude.TargetResourceType != null && yInclude.TargetResourceType == xInclude.TargetResourceType)
                            || (yInclude.TargetResourceType != null && xTargetTypes != null && xTargetTypes.Contains(yInclude.TargetResourceType)))
                        {
                            return 1;
                        }

                        // one of y's target types matches x's source type => x > y
                        if (yInclude.TargetResourceType == null && xTargetTypes != null && yTargetTypes.Contains(xInclude.SourceResourceType))
                        {
                            return 1;
                        }

                        return -1;
                    }
                }
                else if (y.Kind == TableExpressionKind.Include)
                {
                    // Non-include should come before includes
                    return -1;
                }

                return 0; // Keep the same order
            }
        }
    }
}
