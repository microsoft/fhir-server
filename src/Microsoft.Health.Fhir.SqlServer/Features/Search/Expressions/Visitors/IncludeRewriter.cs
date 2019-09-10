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

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 1 || expression.TableExpressions.All(e => e.Kind != TableExpressionKind.Include))
            {
                return expression;
            }

            bool containsInclude = false;

            List<TableExpression> reorderedExpressions = expression.TableExpressions.OrderByDescending(t =>
            {
                switch (t.SearchParameterQueryGenerator)
                {
                    case IncludeQueryGenerator _:
                        containsInclude = true;
                        return 0;
                    default:
                        return 10;
                }
            }).ToList();

            if (containsInclude)
            {
                reorderedExpressions.Add(IncludeUnionAllExpression);
            }

            return new SqlRootExpression(reorderedExpressions, expression.DenormalizedExpressions);
        }
    }
}
