// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// For FHIR <c>date</c>-typed search parameters whose underlying field is exactly a date (i.e.
    /// <see cref="Microsoft.Health.Fhir.Core.Models.SearchParameterInfo.IsDateOnly"/> is true), every stored row has
    /// <c>StartDateTime</c> = startOfDay(d) and <c>EndDateTime</c> = endOfDay(d). The two-predicate overlap form
    /// emitted by Core for an equality query (<c>Start</c> &gt;= startOfDay AND <c>End</c> &lt;= endOfDay) is therefore
    /// mathematically equivalent to a single-column equality on <c>EndDateTime</c>. SQL Server's optimizer cannot
    /// prove this from the predicates alone, so we collapse the pattern here to give the optimizer an obvious
    /// single-column index seek over <c>IX_SearchParamId_EndDateTime_StartDateTime_...</c>.
    ///
    /// This rewriter must run BEFORE <see cref="DateTimeEqualityRewriter"/> so the input pattern still has only two
    /// predicates. After this rewriter fires, the equality rewriter no-ops because the pattern it looks for is gone.
    ///
    /// Composite parameters and range operators (gt/lt/ge/le/sa/eb) are out of scope; this rewriter passes through
    /// unchanged in those cases.
    /// </summary>
    internal class DateOnlyEqualityRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly DateOnlyEqualityRewriter Instance = new DateOnlyEqualityRewriter();

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (!expression.Parameter.IsDateOnly)
            {
                return expression;
            }

            if (expression.Expression is not MultiaryExpression multiary ||
                multiary.MultiaryOperation != MultiaryOperator.And ||
                multiary.Expressions.Count != 2 ||
                multiary.Expressions[0] is not BinaryExpression first ||
                multiary.Expressions[1] is not BinaryExpression second ||
                first.ComponentIndex != second.ComponentIndex)
            {
                return expression;
            }

            // Pattern emitted by Core for ?param=YYYY-MM-DD (equality):
            //   (StartDateTime >= startOfDay) AND (EndDateTime <= endOfDay)
            // Either order is permissible, so check both arrangements.
            BinaryExpression startGe = null;
            BinaryExpression endLe = null;

            if (first.FieldName == FieldName.DateTimeStart && first.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                second.FieldName == FieldName.DateTimeEnd && second.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                startGe = first;
                endLe = second;
            }
            else if (second.FieldName == FieldName.DateTimeStart && second.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                     first.FieldName == FieldName.DateTimeEnd && first.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                startGe = second;
                endLe = first;
            }
            else
            {
                return expression;
            }

            // VALUE-SHAPE guard: confirm the constants describe exactly one calendar day.
            // SearchComparator.Eq always produces startOfDay (TimeOfDay == 0) and endOfDay == startOfDay + 1 day - 1 tick.
            // SearchComparator.Ap emits the identical AST structure but with constants shifted by an approximate delta,
            // so endValue != startValue.AddDays(1).AddTicks(-1). We must NOT collapse Ap queries because the resulting
            // EndDateTime equality value would not match any stored row (all stored rows have endOfDay, not approxEnd).
            if (startGe.Value is not DateTimeOffset startValue || endLe.Value is not DateTimeOffset endValue)
            {
                return expression;
            }

            if (startValue.TimeOfDay != TimeSpan.Zero ||
                endValue != startValue.AddDays(1).AddTicks(-1))
            {
                return expression;
            }

            // Replace the two-column overlap with a single-column equality on EndDateTime.
            // EndDateTime is chosen (not StartDateTime) so SQL prefers IX_SearchParamId_EndDateTime_StartDateTime_...
            // which empirically produces the better plan for this workload.
            var collapsed = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, endLe.ComponentIndex, endLe.Value);

            return new SearchParameterExpression(expression.Parameter, collapsed);
        }
    }
}
