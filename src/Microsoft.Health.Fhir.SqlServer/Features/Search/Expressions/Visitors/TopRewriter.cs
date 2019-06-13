// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal class TopRewriter : SqlExpressionRewriter<SearchOptions>
    {
        public static readonly TopRewriter Instance = new TopRewriter();

        private static readonly TableExpression TopTableExpression = new TableExpression(null, null, null, TableExpressionKind.Top);

        public override Expression VisitSqlRoot(SqlRootExpression expression, SearchOptions context)
        {
            if (context.CountOnly || expression.TableExpressions.Count == 0)
            {
                return expression;
            }

            var newNormalizedPredicates = new List<TableExpression>(expression.TableExpressions.Count + 1);
            newNormalizedPredicates.AddRange(expression.TableExpressions);
            newNormalizedPredicates.Add(TopTableExpression);

            return new SqlRootExpression(newNormalizedPredicates, expression.DenormalizedExpressions);
        }
    }
}
