// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Combines <see cref="SearchParamTableExpressions"/>s that are over the same DateTime search parameter.
    /// For example, without this class, ?issued=ge2024-04-22T00:00:00&amp;issued=lt2024-04-23T00:00:00 will end up as separate table expressions,
    /// but the query will be much more efficient if they are combined.
    /// </summary>
    internal class DateTimeTableExpressionCombiner : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly DateTimeTableExpressionCombiner Instance = new DateTimeTableExpressionCombiner();

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            // This rewriter is a little heavier, so we bail out early if we can.
            if (expression.SearchParamTableExpressions.Count <= 1)
            {
                return expression;
            }

            // now we look for pairs of expressions that are over the same search parameter
            // that are also DateTime expressions
            var groupedSearchParams = expression.SearchParamTableExpressions.GroupBy(
                p => (p.Predicate as SearchParameterExpression)?.Parameter,
                p => p);

            var newSearchParamExpressions = new List<SearchParamTableExpression>(expression.SearchParamTableExpressions);

            foreach (var group in groupedSearchParams)
            {
                // This is a targeted change, so we are carefully limiting the
                // expressions that we will apply this change to.
                // For now, only DateTime expressions are supported, where
                // there are exactly 2 expressions over the same search parameter.
                if ((group.Key?.Type != ValueSets.SearchParamType.Date) || (group.Count() != 2))
                {
                    continue;
                }

                // one of the expression.Predicate must be GreaterThanOrEqual, the other LessThanOrEqual
                if (group.Any(p =>
                    {
                        var searchParameterExpression = p.Predicate as SearchParameterExpression;
                        var binaryExpression = searchParameterExpression?.Expression as BinaryExpression;
                        return (binaryExpression?.BinaryOperator == BinaryOperator.GreaterThanOrEqual || binaryExpression?.BinaryOperator == BinaryOperator.GreaterThan)
                               && binaryExpression?.FieldName == FieldName.DateTimeEnd;
                    }) && group.Any(p =>
                    {
                        var searchParameterExpression = p.Predicate as SearchParameterExpression;
                        var binaryExpression = searchParameterExpression?.Expression as BinaryExpression;
                        return (binaryExpression?.BinaryOperator == BinaryOperator.LessThanOrEqual || binaryExpression?.BinaryOperator == BinaryOperator.LessThan)
                               && binaryExpression?.FieldName == FieldName.DateTimeStart;
                    }))
                {
                    // Now we want to create a new multiary expression that combines the two DateTime expressions
                    var multiaryExpression = new MultiaryExpression(
                        MultiaryOperator.And,
                        group.Select(p => (p.Predicate as SearchParameterExpression).Expression).ToArray());

                    var combineSearchParamExpression = new SearchParameterExpression(
                        group.Key,
                        multiaryExpression);

                    var combinedExpression = new SearchParamTableExpression(
                        group.First().QueryGenerator,
                        combineSearchParamExpression,
                        SearchParamTableExpressionKind.Normal);

                    // now remove the original expressions in this group from expression.SearchParamTableExpressions
                    // and add the new combined expression
                    foreach (var searchParamExpression in group)
                    {
                        newSearchParamExpressions.Remove(searchParamExpression);
                    }

                    newSearchParamExpressions.Add(combinedExpression);
                }
            }

            return new SqlRootExpression(newSearchParamExpressions, expression.ResourceTableExpressions);
        }
    }
}
