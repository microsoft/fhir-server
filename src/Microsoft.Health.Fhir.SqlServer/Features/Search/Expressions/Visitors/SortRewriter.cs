// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// It creates the correct generator and populates the predicates for sort parameters.
    /// </summary>
    internal class SortRewriter : SqlExpressionRewriter<SearchOptions>
    {
        private readonly SearchParamTableExpressionQueryGeneratorFactory _searchParamTableExpressionQueryGeneratorFactory;

        public SortRewriter(SearchParamTableExpressionQueryGeneratorFactory searchParamTableExpressionQueryGeneratorFactory)
        {
            _searchParamTableExpressionQueryGeneratorFactory = searchParamTableExpressionQueryGeneratorFactory;
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, SearchOptions context)
        {
            if (context.CountOnly)
            {
                return expression;
            }

            // Proceed if we sort params were requested.
            if (context.Sort.Count == 0)
            {
                return expression;
            }

            // _lastUpdated sort param is handled differently than others, because it can be
            // inferred directly from the resource table itself.
            if (context.Sort[0].searchParameterInfo.Code == KnownQueryParameterNames.LastUpdated)
            {
                return expression;
            }

            bool matchFound = false;
            for (int i = 0; i < expression.SearchParamTableExpressions.Count; i++)
            {
                Expression updatedExpression = expression.SearchParamTableExpressions[i].Predicate.AcceptVisitor(this, context);
                if (updatedExpression == null)
                {
                    matchFound = true;
                    break;
                }
            }

            SearchParamTableExpressionKind sortKind = matchFound ? SearchParamTableExpressionKind.SortWithFilter : SearchParamTableExpressionKind.Sort;

            var queryGenerator = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);

            var newTableExpressions = new List<SearchParamTableExpression>(expression.SearchParamTableExpressions.Count + 1);
            newTableExpressions.AddRange(expression.SearchParamTableExpressions);

            newTableExpressions.Add(new SearchParamTableExpression(queryGenerator, new SortExpression(context.Sort[0].searchParameterInfo), sortKind));

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, SearchOptions context)
        {
            if (context.Sort.Count > 0)
            {
                if (expression.Parameter.Equals(context.Sort[0].searchParameterInfo))
                {
                    // We are returning null here to notify that we have found a SearchParameterExpression
                    // for the same search parameter used for sort.
                    return null;
                }
            }

            return expression;
        }
    }
}
