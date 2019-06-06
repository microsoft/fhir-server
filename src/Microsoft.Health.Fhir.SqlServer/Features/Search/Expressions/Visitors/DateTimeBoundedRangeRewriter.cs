// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
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
                    new BinaryExpression(left.BinaryOperator, FieldName.DateTimeStart, left.ComponentIndex, ((DateTimeOffset)left.Value).AddTicks(-TimeSpan.TicksPerDay)),
                    new BinaryExpression(right.BinaryOperator, FieldName.DateTimeStart, right.ComponentIndex, right.Value));
            }

            return expression;
        }

        private class Scout : ExpressionRewriterWithInitialContext<object>
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
                if (expression.MultiaryOperation == MultiaryOperator.And &&
                    expression.Expressions.Count == 2 &&
                    expression.Expressions[0] is BinaryExpression left &&
                    expression.Expressions[1] is BinaryExpression right)
                {
                    if (left.BinaryOperator > right.BinaryOperator)
                    {
                        var tmp = left;
                        left = right;
                        right = tmp;
                    }

                    if (left.FieldName == FieldName.DateTimeEnd &&
                        right.FieldName == FieldName.DateTimeStart &&
                        (left.BinaryOperator == BinaryOperator.GreaterThanOrEqual || left.BinaryOperator == BinaryOperator.GreaterThan) &&
                        (right.BinaryOperator == BinaryOperator.LessThanOrEqual || right.BinaryOperator == BinaryOperator.LessThan))
                    {
                        return Expression.And(
                            Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, left.ComponentIndex, true),
                            new BinaryExpression(left.BinaryOperator, left.FieldName, left.ComponentIndex, left.Value),
                            new BinaryExpression(right.BinaryOperator, right.FieldName, right.ComponentIndex, right.Value));
                    }
                }

                return expression;
            }
        }
    }
}
