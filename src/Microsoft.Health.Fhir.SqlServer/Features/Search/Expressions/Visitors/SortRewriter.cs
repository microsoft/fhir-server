// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// It creates the correct generator and populates the predicates for sort parameters.
    /// </summary>
    internal class SortRewriter : SqlExpressionRewriter<SqlSearchOptions>
    {
        private readonly SearchParamTableExpressionQueryGeneratorFactory _searchParamTableExpressionQueryGeneratorFactory;

        public SortRewriter(SearchParamTableExpressionQueryGeneratorFactory searchParamTableExpressionQueryGeneratorFactory)
        {
            _searchParamTableExpressionQueryGeneratorFactory = searchParamTableExpressionQueryGeneratorFactory;
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, SqlSearchOptions context)
        {
            // If we only need the count, we don't want to execute any sort specific queries.
            if (context.CountOnly)
            {
                return expression;
            }

            // Proceed if no sort params were requested.
            if (context.Sort.Count == 0)
            {
                return expression;
            }

            // _type and _lastUpdated sort params are handled differently than others, because they can be
            // inferred directly from the resource table itself.
            if (context.Sort.All(s => s.searchParameterInfo.Name is SearchParameterNames.ResourceType or SearchParameterNames.LastUpdated))
            {
                return expression;
            }

            // Check if the parameter being sorted on is also part of another parameter for the search.
            // If the parameter being sorted on is part of a filter then we don't need to run the seperate search for resources that are missing a value for the field being sorted on.
            // If the parameter being sorted on is not part of a filter we need to run a seperate search to get resources that don't have a value for the field being sorted on.
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
            ContinuationToken continuationToken;

            if (context.UseIndexedPaging)
            {
                continuationToken = null;
            }
            else
            {
                continuationToken = ContinuationToken.FromString(context.ContinuationToken);
            }

            if (!matchFound)
            {
                // We are running a sort query where the parameter by which we are sorting
                // is not present as part of other search parameters in the query.

                // Check whether we have to execute the second phase of the search for a sort query.
                // This can occur when SearchService decides to run a second search while processing the current query.
                // Or it could be a query from the client with a hardcoded "special" continuation token.
                if (context.SortQuerySecondPhase ||
                    (continuationToken != null &&
                        continuationToken.ResourceSurrogateId == 0 &&
                        continuationToken.SortValue == SqlSearchConstants.SortSentinelValueForCt))
                {
                    if (continuationToken != null)
                    {
                        context.ContinuationToken = null;
                    }

                    if (context.Sort[0].sortOrder == SortOrder.Descending)
                    {
                        // For descending order, the second phase of the sort query deals with searching
                        // for resources that do not have a value for the _sort parameter.
                        var missingExpression = Expression.MissingSearchParameter(context.Sort[0].searchParameterInfo, isMissing: true);
                        var queryGenForMissing = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);
                        var notExistsExpression = new SearchParamTableExpression(
                            queryGenForMissing,
                            missingExpression,
                            SearchParamTableExpressionKind.NotExists);

                        newTableExpressions.Add(notExistsExpression);

                        return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
                    }

                    // For ascending, the second phase of the sort query deals with searching
                    // for resources that have a value for the _sort parameter. So we will generate
                    // the appropriate Sort expression below.
                }
                else if (continuationToken != null && continuationToken.SortValue == null)
                {
                    // We have a ct for resourceid but not for the sort value.
                    // This means we are paging through resources that do not have values for the _sort parameter.
                    var missingExpression = Expression.MissingSearchParameter(context.Sort[0].searchParameterInfo, isMissing: true);
                    var queryGenForMissing = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);
                    var notExistsExpression = new SearchParamTableExpression(
                        queryGenForMissing,
                        missingExpression,
                        SearchParamTableExpressionKind.NotExists);

                    newTableExpressions.Add(notExistsExpression);

                    return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
                }
                else if (continuationToken == null)
                {
                    // This means we are in the first "phase" of searching for resources for the _sort query.
                    // For ascending order, we will search for resources that do not have a value for the
                    // corresponding _sort parameter.
                    if (context.Sort[0].sortOrder == SortOrder.Ascending)
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

                    // For descending order, we will search for resources that have a value for the
                    // corresponding _sort parameter. We will generate the appropriate Sort expression below.
                }
            }

            SearchParamTableExpressionKind sortKind = matchFound ? SearchParamTableExpressionKind.SortWithFilter : SearchParamTableExpressionKind.Sort;
            if (sortKind == SearchParamTableExpressionKind.SortWithFilter)
            {
                context.IsSortWithFilter = true;
            }

            var queryGenerator = _searchParamTableExpressionQueryGeneratorFactory.GetSearchParamTableExpressionQueryGenerator(context.Sort[0].searchParameterInfo);

            newTableExpressions.Add(new SearchParamTableExpression(queryGenerator, new SortExpression(context.Sort[0].searchParameterInfo), sortKind));

            return new SqlRootExpression(newTableExpressions, expression.ResourceTableExpressions);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, SqlSearchOptions context)
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
