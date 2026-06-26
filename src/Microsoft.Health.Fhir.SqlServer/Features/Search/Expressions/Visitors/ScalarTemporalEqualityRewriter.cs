// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// For allow-listed FHIR date search parameters where each resource stores a single date element
    /// (currently <c>birthdate</c>), every stored row has <c>DateTimeStart</c> and <c>DateTimeEnd</c>
    /// representing the date value expanded to its period boundaries. Core emits the spec-compliant
    /// containment form for equality (<c>DateTimeStart &gt;= periodStart AND DateTimeEnd &lt;= periodEnd</c>):
    /// the resource's stored period must sit inside the query window.
    ///
    /// For an exact UTC calendar day, this rewriter collapses that containment to a single predicate on
    /// <c>DateTimeEnd</c> combined with <c>IsLongerThanADay = false</c>. A stored period longer than one
    /// day can never be contained within a one-day window, so those rows are excluded by construction —
    /// no <see cref="UnionExpression"/> / day-split is required, and the planner can use the filtered index
    /// on single-day rows. Month/year-precision queries, single-sided predicates, range operators,
    /// approximate (<c>ap</c>) expressions, composite parameters, and non-allow-listed parameters all pass
    /// through unchanged.
    ///
    /// This rewriter runs on the Core expression tree before the Core-&gt;SQL conversion, and only when BOTH
    /// the scalar-temporal flag and the system-wide FHIR date containment flag are enabled. Its End-only
    /// output is result-equivalent to the legacy overlap form only for exact-day stored birthdates; for
    /// partial-precision stored values it equals containment (not overlap), which is why it is gated on the
    /// containment flag. Because its output is a terminal <c>DateTimeEnd</c> equality (not the start/end
    /// containment shape), <see cref="DateTimeEqualityRewriter"/> never re-touches it.
    /// </summary>
    internal class ScalarTemporalEqualityRewriter : SqlExpressionRewriterWithInitialContext<bool>
    {
        internal static readonly ScalarTemporalEqualityRewriter Instance = new ScalarTemporalEqualityRewriter();

        // NOTE: This list is only for parameters that are the same across all FHIR versions. Before adding parameters
        // this approach may need to be revisited to support version-specific allow lists.
        private static readonly HashSet<string> _allowList = new HashSet<string>(StringComparer.Ordinal)
        {
            "http://hl7.org/fhir/SearchParameter/individual-birthdate",
        };

        private enum Precision
        {
            NotRewritable,
            ExactDay,
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, bool context)
        {
            // 1. Only allow-listed scalar date parameters are eligible.
            if (!IsActivatedScalarTemporalParameter(expression))
            {
                return expression;
            }

            // 2. The inner expression must be the two-predicate equality pattern Core emits.
            if (!TryMatchEqualityPattern(expression.Expression, out BinaryExpression startPredicate, out BinaryExpression endPredicate))
            {
                return expression;
            }

            // 3. The predicate operands must be concrete DateTimeOffset values so we can inspect
            //    the start/end boundaries and decide whether they cover exactly one calendar day.
            //    Anything else (null, string, partial date) is not something we can classify, so pass through.
            if (startPredicate.Value is not DateTimeOffset startValue ||
                endPredicate.Value is not DateTimeOffset endValue)
            {
                return expression;
            }

            // 4. Classify precision and build the matching rewrite, or pass through.
            //    Day-precision collapses to a single DateTimeEnd predicate (no UNION).
            return ClassifyPrecision(startValue, endValue) switch
            {
                Precision.ExactDay => BuildEndOnlyPredicate(expression.Parameter, endPredicate),
                _ => expression,
            };
        }

        internal static bool IsActivatedScalarTemporalParameter(SearchParameterExpression expression)
        {
            var p = expression?.Parameter;
            if (p == null)
            {
                return false;
            }

            bool isScalarDate = p.Type == SearchParamType.Date && (p.Component == null || p.Component.Count == 0);
            bool isAllowListed = p.Url != null && _allowList.Contains(p.Url.OriginalString);

            return isScalarDate && isAllowListed;
        }

        /// <summary>
        /// Matches the shape <c>DateTimeStart &gt;= X AND DateTimeEnd &lt;= Y</c> (either operand order).
        /// On success, returns the two predicates normalized so <paramref name="startGe"/> is always the
        /// <c>DateTimeStart &gt;=</c> side and <paramref name="endLe"/> is always the <c>DateTimeEnd &lt;=</c> side.
        /// </summary>
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
                multiary.Expressions[0] is not BinaryExpression a ||
                multiary.Expressions[1] is not BinaryExpression b ||
                a.ComponentIndex != b.ComponentIndex)
            {
                return false;
            }

            // Operands may appear in either order; pick the one that is the start-ge predicate.
            if (IsStartGe(a) && IsEndLe(b))
            {
                startGe = a;
                endLe = b;
                return true;
            }

            if (IsStartGe(b) && IsEndLe(a))
            {
                startGe = b;
                endLe = a;
                return true;
            }

            return false;
        }

        private static Precision ClassifyPrecision(DateTimeOffset start, DateTimeOffset end)
        {
            // Both endpoints must be UTC, and start must sit on a UTC midnight, before any precision applies.
            if (!IsUtcMidnight(start) || !IsUtc(end))
            {
                return Precision.NotRewritable;
            }

            if (end == start.AddDays(1).AddTicks(-1))
            {
                return Precision.ExactDay;
            }

            return Precision.NotRewritable;
        }

        // Under FHIR-spec containment, an exact-day eq matches only resources whose stored period is
        // contained within the one-day query window. Birthdate rows are stored as exactly one full UTC
        // day, so the containment predicates collapse to a single equality on DateTimeEnd combined with
        // IsLongerThanADay = false. A stored period longer than one day can never be contained within a
        // single-day window, so those rows are excluded by construction — no UNION ALL / day-split is
        // needed, and the planner can use the filtered index on single-day rows.
        private static SearchParameterExpression BuildEndOnlyPredicate(
            SearchParameterInfo parameter,
            BinaryExpression endPredicate)
        {
            return new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, endPredicate.ComponentIndex, false),
                    new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, endPredicate.ComponentIndex, endPredicate.Value)));
        }

        private static bool IsStartGe(BinaryExpression be) =>
            be.FieldName == FieldName.DateTimeStart && be.BinaryOperator == BinaryOperator.GreaterThanOrEqual;

        private static bool IsEndLe(BinaryExpression be) =>
            be.FieldName == FieldName.DateTimeEnd && be.BinaryOperator == BinaryOperator.LessThanOrEqual;

        private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

        private static bool IsUtcMidnight(DateTimeOffset value) => IsUtc(value) && value.TimeOfDay == TimeSpan.Zero;
    }
}
