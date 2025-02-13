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
    internal class IncludesOperationRewriter : IncludeRewriter
    {
        internal static new readonly IncludesOperationRewriter Instance = new IncludesOperationRewriter();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            if (expression == null
                || expression.SearchParamTableExpressions.Count == 1
                || expression.SearchParamTableExpressions.All(e => e.Kind != SearchParamTableExpressionKind.Include))
            {
                return expression;
            }

            // SearchParamTableExpressions contains at least one Include expression
            var nonIncludeExpressions = expression.SearchParamTableExpressions.Where(e => e.Kind != SearchParamTableExpressionKind.Include).ToList();
            var includeExpressions = expression.SearchParamTableExpressions.Where(e => e.Kind == SearchParamTableExpressionKind.Include).ToList();

            // Sort include expressions if there is an include iterate expression
            // Order so that include iterate expression appear after the expressions they select from
            IEnumerable<SearchParamTableExpression> sortedIncludeExpressions = includeExpressions;
            if (includeExpressions.Any(e => ((IncludeExpression)e.Predicate).Iterate))
            {
                IEnumerable<SearchParamTableExpression> nonIncludeIterateExpressions = includeExpressions.Where(e => !((IncludeExpression)e.Predicate).Iterate);
                List<SearchParamTableExpression> includeIterateExpressions = includeExpressions.Where(e => ((IncludeExpression)e.Predicate).Iterate).ToList();
                sortedIncludeExpressions = nonIncludeIterateExpressions.Concat(SortIncludeIterateExpressions(includeIterateExpressions));
            }

            // Add sorted include expressions after all other expressions
            var reorderedExpressions = nonIncludeExpressions.Concat(sortedIncludeExpressions).Concat(new[] { IncludeUnionAllExpression, IncludeLimitExpression }).ToList();
            return new SqlRootExpression(reorderedExpressions, expression.ResourceTableExpressions);
        }
    }
}
