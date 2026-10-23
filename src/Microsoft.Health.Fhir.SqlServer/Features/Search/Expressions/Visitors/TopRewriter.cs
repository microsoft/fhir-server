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
            // The Top table expression is what assigns the main CTE, and the include table expressions
            // reference that CTE unconditionally. An $includes operation therefore still needs it even when
            // only a count is requested. A regular count-only search has no include table expressions, so
            // the main CTE is never referenced and Top can be skipped.
            if ((context.CountOnly && !context.IsIncludesOperation) ||
                expression.SearchParamTableExpressions.Count == 0 ||
                context is SqlSearchOptions { PreparedVectorQuery: not null })
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
