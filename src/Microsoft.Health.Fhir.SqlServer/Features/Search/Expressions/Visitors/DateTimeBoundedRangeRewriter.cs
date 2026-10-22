// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Looks for the pattern (And (FieldGreaterThan DateTimeEnd X) (FieldLessThan DateTimeStart Y))
    /// This is produced when the search expression ?date=gtX&amp;date=ltY after the <see cref="TableExpressionCombiner"/>
    /// visitor has run. The problem with this expression is that it can be very expensive because of the unbound ranges
    /// that have no relation to one another. One way we can optimize this is to notice that the vast majority of datetime ranges in
    /// the database will be less than a day. This means that for most of the data, we can apply a fixed range on DateTimeStart, knowing
    /// that it will greater than (X - 1 day) in addition to always being less than Y. We can create a filtered nonclustered
    /// index where the range is greater than one day. Over this index, we'll do the original query, but it will be on a much smaller
    /// set of row.
    /// </summary>
    internal class DateTimeBoundedRangeRewriter : ConcatenationRewriter
    {
        internal static readonly DateTimeBoundedRangeRewriter Instance = new DateTimeBoundedRangeRewriter();

        public DateTimeBoundedRangeRewriter()
            : base(new Scout())
        {
        }

        public override Expression VisitSqlRoot(SqlRootExpression expression, object context)
        {
            List<SearchParamTableExpression> tables = null;
            for (var i = 0; i < expression.SearchParamTableExpressions.Count; i++)
            {
                var table = expression.SearchParamTableExpressions[i];
                if (table.Kind == SearchParamTableExpressionKind.Normal &&
                    table.ChainLevel > 0 &&
                    ReferenceEquals(table.QueryGenerator, DateTimeQueryGenerator.Instance) &&
                    table.Predicate is SearchParameterExpression parameter &&
                    ScalarTemporalEqualityRewriter.IsActivatedScalarTemporalParameter(parameter) &&
                    parameter.Expression is MultiaryExpression overlap &&
                    TryMatchOverlapPattern(overlap, out var left, out var right) &&
                    left.ComponentIndex == right.ComponentIndex)
                {
                    var predicate = CreateLegacyEquivalentPredicate(parameter.Parameter, left, right);
                    EnsureAllocatedAndPopulated(ref tables, expression.SearchParamTableExpressions, i);
                    table = new SearchParamTableExpression(table.QueryGenerator, predicate, table.Kind, table.ChainLevel);
                }

                tables?.Add(table);
            }

            return base.VisitSqlRoot(
                tables == null ? expression : new SqlRootExpression(tables, expression.ResourceTableExpressions, expression.SmartCompartmentMembership),
                context);
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            if (expression.MultiaryOperation == MultiaryOperator.And &&
                expression.Expressions.Count == 3 &&
                expression.Expressions[0] is BinaryExpression isLong &&
                isLong.FieldName == SqlFieldName.DateTimeIsLongerThanADay)
            {
                var left = (BinaryExpression)expression.Expressions[1];
                var right = (BinaryExpression)expression.Expressions[2];
                return Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, left.ComponentIndex, false),
                    new BinaryExpression(left.BinaryOperator, FieldName.DateTimeEnd, left.ComponentIndex, left.Value),
                    CreateBoundedStartPredicate(left),
                    new BinaryExpression(right.BinaryOperator, FieldName.DateTimeStart, right.ComponentIndex, right.Value));
            }

            return expression;
        }

        private static BinaryExpression CreateBoundedStartPredicate(BinaryExpression left)
        {
            var leftValue = (DateTimeOffset)left.Value;
            DateTimeOffset boundedStart = leftValue.SafeAddTicks(-TimeSpan.TicksPerDay);
            BinaryOperator boundedStartOperator = left.BinaryOperator == BinaryOperator.GreaterThan &&
                leftValue < DateTimeOffset.MinValue.AddTicks(TimeSpan.TicksPerDay)
                ? BinaryOperator.GreaterThanOrEqual
                : left.BinaryOperator;

            return new BinaryExpression(boundedStartOperator, FieldName.DateTimeStart, left.ComponentIndex, boundedStart);
        }

        private static SearchParameterExpression CreateLegacyEquivalentPredicate(
            SearchParameterInfo parameter,
            BinaryExpression left,
            BinaryExpression right)
        {
            return Expression.SearchParameter(
                parameter,
                Expression.And(
                    left,
                    right,
                    Expression.Or(
                        Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, left.ComponentIndex, true),
                        CreateBoundedStartPredicate(left))));
        }

        private static bool TryMatchOverlapPattern(MultiaryExpression expression, out BinaryExpression left, out BinaryExpression right)
        {
            left = null;
            right = null;
            if (expression.MultiaryOperation != MultiaryOperator.And ||
                expression.Expressions.Count != 2 ||
                expression.Expressions[0] is not BinaryExpression first ||
                expression.Expressions[1] is not BinaryExpression second)
            {
                return false;
            }

            left = first;
            right = second;
            if (left.BinaryOperator > right.BinaryOperator)
            {
                (left, right) = (right, left);
            }

            return left.FieldName == FieldName.DateTimeEnd &&
                right.FieldName == FieldName.DateTimeStart &&
                (left.BinaryOperator == BinaryOperator.GreaterThanOrEqual || left.BinaryOperator == BinaryOperator.GreaterThan) &&
                (right.BinaryOperator == BinaryOperator.LessThanOrEqual || right.BinaryOperator == BinaryOperator.LessThan);
        }

        private class Scout : SqlExpressionRewriterWithInitialContext<object>
        {
            public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
            {
                // for now, we don't apply this optimization to composite parameters

                if (expression.Parameter.Type == SearchParamType.Date &&
                    expression.Expression is MultiaryExpression)
                {
                    return base.VisitSearchParameter(expression, context);
                }

                return expression;
            }

            public override Expression VisitMultiary(MultiaryExpression expression, object context)
            {
                if (TryMatchOverlapPattern(expression, out var left, out var right))
                {
                    return Expression.And(
                        Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, left.ComponentIndex, true),
                        new BinaryExpression(left.BinaryOperator, left.FieldName, left.ComponentIndex, left.Value),
                        new BinaryExpression(right.BinaryOperator, right.FieldName, right.ComponentIndex, right.Value));
                }

                return expression;
            }
        }
    }
}
