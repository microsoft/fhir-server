// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewriter used to add an All TableExpression to serve as a seed for match results when the TableExpressions in a SqlRootExpression
    /// consists solely of Include expressions.
    /// </summary>
    internal class IncludeMatchSeedRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly IncludeMatchSeedRewriter Instance = new IncludeMatchSeedRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            if (!expression.TableExpressions.All(te => te.Kind == TableExpressionKind.Include))
            {
                return expression;
            }

            var newTableExpressions = new List<TableExpression>(expression.TableExpressions.Count + 1);

            Expression resourceExpression = Expression.And(expression.ResourceExpressions);
            var allExpression = new TableExpression(null, resourceExpression, TableExpressionKind.All);

            newTableExpressions.Add(allExpression);
            newTableExpressions.AddRange(expression.TableExpressions);

            return new SqlRootExpression(newTableExpressions, Array.Empty<SearchParameterExpression>());
        }
    }
}
