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

        private static readonly SearchParamTableExpression _topSearchParamTableExpression = new SearchParamTableExpression(null, null, SearchParamTableExpressionKind.Top);

        public override Expression VisitSqlRoot(SqlRootExpression expression, SearchOptions context)
        {
            if (context.CountOnly || expression.SearchParamTableExpressions.Count == 0)
            {
                return expression;
            }

            var newTableExpressions = new List<SearchParamTableExpression>(expression.SearchParamTableExpressions.Count + 1);
            newTableExpressions.AddRange(expression.SearchParamTableExpressions);

            newTableExpressions.Add(_topSearchParamTableExpression);

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }
    }
}
