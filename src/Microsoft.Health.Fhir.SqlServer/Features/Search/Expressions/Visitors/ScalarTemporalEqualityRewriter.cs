// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// For allow-listed scalar temporal FHIR search parameters (parameters where each resource stores a single
    /// date/dateTime instant, e.g. <c>birthdate</c>), every stored row has <c>StartDateTime</c> and
    /// <c>EndDateTime</c> that represent one calendar instant expanded to period boundaries. The two-predicate
    /// overlap form emitted by Core for equality (<c>DateTimeStart &gt;= periodStart AND DateTimeEnd &lt;= periodEnd</c>)
    /// can be collapsed to predicates on <c>DateTimeEnd</c> only for precisions where the rewrite is safe.
    ///
    /// <list type="bullet">
    ///   <item>Exact UTC calendar day: collapses to <c>DateTimeEnd = endOfDay</c>.</item>
    ///   <item>Exact UTC calendar year: collapses to <c>DateTimeEnd &gt;= yearStart AND DateTimeEnd &lt;= yearEnd</c>.</item>
    ///   <item>Exact UTC calendar month: passes through unchanged. A month-precision query must still match
    ///     stored rows whose value has only year precision (e.g. <c>birthDate=2016</c> stored with
    ///     <c>EndDateTime=2016-12-31T23:59:59.9999999Z</c>), so collapsing to a DateTimeEnd range would
    ///     produce false negatives.</item>
    ///   <item>Approximate (<c>ap</c>) expressions with non-boundary constants pass through unchanged.</item>
    /// </list>
    ///
    /// This rewriter must run BEFORE <see cref="DateTimeEqualityRewriter"/> so the input pattern still has only two
    /// predicates. Composite parameters and range operators are out of scope and pass through unchanged.
    /// </summary>
    internal class ScalarTemporalEqualityRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly ScalarTemporalEqualityRewriter Instance = new ScalarTemporalEqualityRewriter();

        private static readonly HashSet<string> _allowList = new HashSet<string>(StringComparer.Ordinal)
        {
            "http://hl7.org/fhir/SearchParameter/individual-birthdate",
        };

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (!TryGetRewriteValues(expression, out BinaryExpression endLe, out DateTimeOffset startValue, out DateTimeOffset endValue))
            {
                return expression;
            }

            if (IsExactDay(startValue, endValue))
            {
                var collapsed = new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, endLe.ComponentIndex, endLe.Value);
                return new SearchParameterExpression(expression.Parameter, collapsed);
            }

            if (IsExactYear(startValue, endValue))
            {
                var range = Expression.And(
                    Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, endLe.ComponentIndex, startValue),
                    Expression.LessThanOrEqual(FieldName.DateTimeEnd, endLe.ComponentIndex, endValue));
                return new SearchParameterExpression(expression.Parameter, range);
            }

            return expression;
        }

        internal static bool IsActivatedScalarTemporalParameter(SearchParameterExpression expression)
        {
            if (expression?.Parameter == null)
            {
                return false;
            }

            var p = expression.Parameter;
            return p.Type == SearchParamType.Date
                && p.IsScalarTemporal
                && (p.Component == null || p.Component.Count == 0)
                && p.Url != null
                && _allowList.Contains(p.Url.OriginalString);
        }

        internal static bool TryMatchEqualityPattern(
            Expression expr,
            out BinaryExpression startGe,
            out BinaryExpression endLe)
        {
            startGe = null;
            endLe = null;

            if (expr is not MultiaryExpression multiary ||
                multiary.MultiaryOperation != MultiaryOperator.And ||
                multiary.Expressions.Count != 2 ||
                multiary.Expressions[0] is not BinaryExpression first ||
                multiary.Expressions[1] is not BinaryExpression second ||
                first.ComponentIndex != second.ComponentIndex)
            {
                return false;
            }

            if (first.FieldName == FieldName.DateTimeStart && first.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                second.FieldName == FieldName.DateTimeEnd && second.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                startGe = first;
                endLe = second;
                return true;
            }

            if (second.FieldName == FieldName.DateTimeStart && second.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                first.FieldName == FieldName.DateTimeEnd && first.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                startGe = second;
                endLe = first;
                return true;
            }

            return false;
        }

        internal static bool WouldRewrite(SearchParameterExpression expression)
        {
            if (!TryGetRewriteValues(expression, out _, out DateTimeOffset startValue, out DateTimeOffset endValue))
            {
                return false;
            }

            return IsRewritablePrecision(startValue, endValue);
        }

        internal static bool IsRewritablePrecision(DateTimeOffset start, DateTimeOffset end) => IsExactDay(start, end) || IsExactYear(start, end);

        private static bool TryGetRewriteValues(
            SearchParameterExpression expression,
            out BinaryExpression endLe,
            out DateTimeOffset startValue,
            out DateTimeOffset endValue)
        {
            endLe = null;
            startValue = default;
            endValue = default;

            if (!IsActivatedScalarTemporalParameter(expression))
            {
                return false;
            }

            if (!TryMatchEqualityPattern(expression.Expression, out BinaryExpression startGe, out endLe))
            {
                return false;
            }

            if (startGe.Value is not DateTimeOffset matchedStartValue || endLe.Value is not DateTimeOffset matchedEndValue)
            {
                return false;
            }

            startValue = matchedStartValue;
            endValue = matchedEndValue;
            return true;
        }

        private static bool IsExactDay(DateTimeOffset start, DateTimeOffset end)
        {
            return start.Offset == TimeSpan.Zero
                && end.Offset == TimeSpan.Zero
                && start.TimeOfDay == TimeSpan.Zero
                && end == start.AddDays(1).AddTicks(-1);
        }

        private static bool IsExactYear(DateTimeOffset start, DateTimeOffset end)
        {
            return start.Offset == TimeSpan.Zero
                && end.Offset == TimeSpan.Zero
                && start.TimeOfDay == TimeSpan.Zero
                && start.Month == 1
                && start.Day == 1
                && end == start.AddYears(1).AddTicks(-1);
        }
    }
}
