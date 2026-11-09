// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// For built-in scalar parameters proven to extract FHIR <c>date</c> leaves, collapses Core's exact-day
    /// equality containment (<c>DateTimeStart &gt;= lo AND DateTimeEnd &lt;= hi</c>) into a single
    /// <c>DateTimeEnd</c> predicate with <c>IsLongerThanADay = false</c>.
    /// Under legacy overlap semantics, retains long stored dates in a disjoint predicate-level OR branch.
    /// No <see cref="UnionExpression"/> or additional table expression is introduced.
    /// Other query precisions, range/<c>ap</c> operators, and composite or unproven parameters
    /// pass through unchanged. Runs before date equality semantics and Core-to-SQL conversion.
    /// </summary>
    internal class ScalarTemporalEqualityRewriter : SqlExpressionRewriterWithInitialContext<bool>
    {
        internal static readonly ScalarTemporalEqualityRewriter Instance = new ScalarTemporalEqualityRewriter();

        private enum Precision
        {
            NotRewritable,
            ExactDay,
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, bool preserveOverlappingLongDates)
        {
            if (!IsActivatedScalarTemporalParameter(expression))
            {
                return expression;
            }

            // The inner expression must be the two-predicate equality pattern Core emits.
            if (!TryMatchEqualityPattern(expression.Expression, out BinaryExpression startPredicate, out BinaryExpression endPredicate))
            {
                return expression;
            }

            // The predicate operands must be concrete DateTimeOffset values so we can inspect
            //    the start/end boundaries and decide whether they cover exactly one calendar day.
            //    Anything else (null, string, partial date) is not something we can classify, so pass through.
            if (startPredicate.Value is not DateTimeOffset startValue ||
                endPredicate.Value is not DateTimeOffset endValue)
            {
                return expression;
            }

            return ClassifyPrecision(startValue, endValue) switch
            {
                Precision.ExactDay => BuildPredicate(expression, endPredicate, preserveOverlappingLongDates),
                _ => expression,
            };
        }

        internal static bool IsActivatedScalarTemporalParameter(SearchParameterExpression expression)
        {
            var p = expression?.Parameter;
            return p != null &&
                p.IsSystemDefined &&
                p.Type == SearchParamType.Date &&
                (p.Component == null || p.Component.Count == 0) &&
                p.TemporalType == TemporalType.Date;
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

            DateTimeOffset nextDay = start.SafeAddDays(1);
            DateTimeOffset inclusiveEnd = nextDay == DateTimeOffset.MaxValue
                ? nextDay
                : nextDay.SafeAddTicks(-1);

            if (end == inclusiveEnd)
            {
                return Precision.ExactDay;
            }

            return Precision.NotRewritable;
        }

        private static SearchParameterExpression BuildPredicate(
            SearchParameterExpression expression,
            BinaryExpression endPredicate,
            bool preserveOverlappingLongDates)
        {
            Expression predicate = Expression.And(
                Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, endPredicate.ComponentIndex, false),
                Expression.Equals(FieldName.DateTimeEnd, endPredicate.ComponentIndex, endPredicate.Value));

            if (preserveOverlappingLongDates)
            {
                // The downstream equality rewriter applies overlap to the retained month/year predicate.
                predicate = Expression.Or(
                    predicate,
                    Expression.And(
                        Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, endPredicate.ComponentIndex, true),
                        expression.Expression));
            }

            return new SearchParameterExpression(expression.Parameter, predicate);
        }

        private static bool IsStartGe(BinaryExpression be) =>
            be.FieldName == FieldName.DateTimeStart && be.BinaryOperator == BinaryOperator.GreaterThanOrEqual;

        private static bool IsEndLe(BinaryExpression be) =>
            be.FieldName == FieldName.DateTimeEnd && be.BinaryOperator == BinaryOperator.LessThanOrEqual;

        private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

        private static bool IsUtcMidnight(DateTimeOffset value) => IsUtc(value) && value.TimeOfDay == TimeSpan.Zero;
    }
}
