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

            // If we only need the count, we don't want to execute any sort specific queries.
            if (context.CountOnly)
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

            var newTableExpressions = new List<SearchParamTableExpression>();
            newTableExpressions.AddRange(expression.SearchParamTableExpressions);
            var continuationToken = ContinuationToken.FromString(context.ContinuationToken);

            if (!matchFound)
            {
                // We are running a sort query where the parameter by which we are sorting
                // is not present as part of other search parameters in the query.

                if (context.SortQuerySecondPhase ||
                    (continuationToken != null && continuationToken.ResourceSurrogateId == 0 && continuationToken.SortValue == "sentinelSortValue"))
                {
                    context.ContinuationToken = null;
                    if (context.Sort[0].sortOrder == SortOrder.Descending)
                    {
                        // Now add the missing expression
                        var missingExpression = Expression.MissingSearchParameter(context.Sort[0].searchParameterInfo, isMissing: true);
                        var queryGenForMissing = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);
                        var notExistsExpression = new SearchParamTableExpression(
                            queryGenForMissing,
                            missingExpression,
                            SearchParamTableExpressionKind.NotExists);

                        newTableExpressions.Add(notExistsExpression);

                        return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
                    }
                    else
                    {
                        // for ascending we don't have anything to do.
                    }
                }
                else if (continuationToken == null || continuationToken.SortValue == null)
                {
                    // TODO: why do we need this null check for continuationToken above?
                    if (context.Sort[0].sortOrder == SortOrder.Ascending)
                    {
                        // Now add the missing expression
                        var missingExpression = Expression.MissingSearchParameter(context.Sort[0].searchParameterInfo, isMissing: true);
                        var queryGenForMissing = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);
                        var notExistsExpression = new SearchParamTableExpression(
                            queryGenForMissing,
                            missingExpression,
                            SearchParamTableExpressionKind.NotExists);

                        newTableExpressions.Add(notExistsExpression);

                        return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
                    }
                    else if (context.Sort[0].sortOrder == SortOrder.Descending)
                    {
                        if (continuationToken != null && continuationToken.SortValue == null)
                        {
                            var missingExpression = Expression.MissingSearchParameter(context.Sort[0].searchParameterInfo, isMissing: true);
                            var queryGenForMissing = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);
                            var notExistsExpression = new SearchParamTableExpression(
                                queryGenForMissing,
                                missingExpression,
                                SearchParamTableExpressionKind.NotExists);

                            newTableExpressions.Add(notExistsExpression);

                            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
                        }
                    }
                }
            }

            SearchParamTableExpressionKind sortKind = matchFound ? SearchParamTableExpressionKind.SortWithFilter : SearchParamTableExpressionKind.Sort;
            if (sortKind == SearchParamTableExpressionKind.SortWithFilter)
            {
                context.SortWithFilter = true;
            }

            var queryGenerator = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);

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
