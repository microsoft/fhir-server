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
    /// Rewriter used to add an All SearchParamTableExpression to serve as a seed for match results when the SearchParamTableExpressions in a SqlRootExpression
    /// consists solely of Include expressions.
    /// Suppose you have an include query like Observation?code=abc&amp;_include=Observation:subject. The include will be based on the CTE that gets the IDs of the first p observations where the code is abc.
    /// Now suppose you have simply Observation?_include=Observation:subject. Here there is no CTE to base the include on, so we inject one that yields the first p Observations.
    /// </summary>
    internal class IncludeMatchSeedRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        public static readonly IncludeMatchSeedRewriter Instance = new IncludeMatchSeedRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression.SearchParamTableExpressions.Count == 0)
            {
                return expression;
            }

            if (!expression.SearchParamTableExpressions.All(te => te.Kind == SearchParamTableExpressionKind.Include))
            {
                return expression;
            }

            var newTableExpressions = new List<SearchParamTableExpression>(expression.SearchParamTableExpressions.Count + 1);

            Expression resourceExpression = Expression.And(expression.ResourceTableExpressions);
            var allExpression = new SearchParamTableExpression(null, resourceExpression, SearchParamTableExpressionKind.All);

            newTableExpressions.Add(allExpression);
            newTableExpressions.AddRange(expression.SearchParamTableExpressions);

            return new SqlRootExpression(newTableExpressions, Array.Empty<SearchParameterExpression>());
        }
    }
}
