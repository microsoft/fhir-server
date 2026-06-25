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
    /// For allow-listed FHIR date search parameters (parameters where each resource stores a single
    /// date element, e.g. <c>birthdate</c>), every stored row has <c>StartDateTime</c> and
    /// <c>EndDateTime</c> that represent the date value expanded to period boundaries. The two-predicate
    /// containment form emitted by Core for equality (<c>DateTimeStart &gt;= periodStart AND DateTimeEnd &lt;= periodEnd</c>)
    /// can be collapsed to predicates on <c>DateTimeEnd</c> only for precisions where the rewrite is safe.
    ///
    /// <list type="bullet">
    ///   <item>Exact UTC calendar day: emits a <see cref="UnionExpression"/> (lowered to SQL <c>UNION ALL</c>)
    ///     with two branches split on <c>IsLongerThanADay</c>. The <c>false</c> branch becomes
    ///     <c>DateTimeEnd = endOfDay</c>; the <c>true</c> branch keeps the original two predicates so
    ///     <see cref="DateTimeEqualityRewriter"/> expands them into the overlap form. This lets the planner
    ///     pick the appropriate filtered index per branch.</item>
    ///   <item>Exact UTC calendar month: passes through unchanged until month-precision rewrite safety has
    ///     dedicated analysis and coverage; the generic containment predicates preserve existing behavior.</item>
    ///   <item>Approximate (<c>ap</c>) expressions with non-boundary constants pass through unchanged.</item>
    /// </list>
    ///
    /// This rewriter must run BEFORE <see cref="DateTimeEqualityRewriter"/> so the input pattern still has only two
    /// predicates. Composite parameters and range operators are out of scope and pass through unchanged.
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

        /// <summary>
        /// Pipeline entry point. Applies the day-split rewrite, EXCEPT when the expression tree contains any
        /// chained or reverse-chained (<c>_has</c>) expression. A day-split <see cref="UnionExpression"/> in the
        /// presence of a chain — nested inside it or sitting beside it as a top-level sibling — generates SQL the
        /// chained generator joins incorrectly (the union aggregate is restricted on the chain SOURCE columns
        /// instead of the chain TARGET), returning zero rows. In that case we skip the rewrite entirely and let
        /// the date predicate fall back to its range form, which the chained generator handles correctly. See ADR-2605.
        /// </summary>
        public static Expression Rewrite(Expression expression)
        {
            if (expression == null)
            {
                return null;
            }

            if (expression.AcceptVisitor(ContainsChainedExpressionVisitor.Instance, (object)null))
            {
                return expression;
            }

            return expression.AcceptVisitor(Instance);
        }

        // Defense-in-depth: Rewrite() already skips the whole tree when any chain is present, so in the normal
        // pipeline this is not reached with a rewritable date nested inside a chain. If the rewriter is ever
        // invoked directly (not via Rewrite), recursing with context=true still prevents a date param nested in
        // a chain from being rewritten into a union.
        public override Expression VisitChained(ChainedExpression expression, bool context)
        {
            Expression visitedExpression = expression.Expression.AcceptVisitor(this, context: true);
            if (ReferenceEquals(visitedExpression, expression.Expression))
            {
                return expression;
            }

            return new ChainedExpression(expression.ResourceTypes, expression.ReferenceSearchParameter, expression.TargetResourceTypes, expression.Reversed, visitedExpression);
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, bool context)
        {
            if (context)
            {
                return expression;
            }

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
            //    Day-precision returns a UnionExpression directly (lowered to SQL UNION ALL).
            return ClassifyPrecision(startValue, endValue) switch
            {
                Precision.ExactDay => BuildDaySplitUnion(expression.Parameter, startPredicate, endPredicate),
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

        // Split along IsLongerThanADay so we only optimize the day-stored subset (longer=false).
        // Stored month/year rows (longer=true) keep the original two predicates, which
        // DateTimeEqualityRewriter then transforms into the overlap form that preserves
        // today's behavior. Emitting as a UnionExpression rather than an OR lets the
        // SQL generator lower this to two sub-SELECTs combined by UNION ALL, so the planner
        // picks the appropriate filtered index per branch instead of relying on OR-expansion.
        // Once the FHIR-containment fix (AB#191826) lands, the longer=true branch becomes
        // dead and this can collapse back to a single SearchParameterExpression on the
        // longer=false predicate.
        private static UnionExpression BuildDaySplitUnion(
            SearchParameterInfo parameter,
            BinaryExpression startPredicate,
            BinaryExpression endPredicate)
        {
            var shortBranch = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, endPredicate.ComponentIndex, false),
                    new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, endPredicate.ComponentIndex, endPredicate.Value)));

            var longBranch = new SearchParameterExpression(
                parameter,
                Expression.And(
                    Expression.Equals(SqlFieldName.DateTimeIsLongerThanADay, endPredicate.ComponentIndex, true),
                    startPredicate,
                    endPredicate));

            return Expression.Union(UnionOperator.All, new Expression[] { shortBranch, longBranch });
        }

        private static bool IsStartGe(BinaryExpression be) =>
            be.FieldName == FieldName.DateTimeStart && be.BinaryOperator == BinaryOperator.GreaterThanOrEqual;

        private static bool IsEndLe(BinaryExpression be) =>
            be.FieldName == FieldName.DateTimeEnd && be.BinaryOperator == BinaryOperator.LessThanOrEqual;

        private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

        private static bool IsUtcMidnight(DateTimeOffset value) => IsUtc(value) && value.TimeOfDay == TimeSpan.Zero;

        /// <summary>
        /// Returns <c>true</c> if the expression tree contains any <see cref="ChainedExpression"/>, covering both
        /// forward chains and reverse chains (<c>_has</c>, distinguished by <see cref="ChainedExpression.Reversed"/>).
        /// Used by <see cref="Rewrite"/> to suppress the day-split union whenever the query involves a chain.
        /// </summary>
        private sealed class ContainsChainedExpressionVisitor : DefaultExpressionVisitor<object, bool>
        {
            internal static readonly ContainsChainedExpressionVisitor Instance = new ContainsChainedExpressionVisitor();

            private ContainsChainedExpressionVisitor()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitChained(ChainedExpression expression, object context) => true;
        }
    }
}
