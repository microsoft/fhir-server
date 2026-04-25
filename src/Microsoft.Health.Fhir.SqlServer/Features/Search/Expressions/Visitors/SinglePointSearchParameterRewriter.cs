// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// SQL expression rewriter that applies the single-point search parameter optimization policy.
    /// Rewrites allowlisted date equality expressions to single-column predicates on DateTimeEnd.
    /// </summary>
    internal class SinglePointSearchParameterRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        internal static readonly SinglePointSearchParameterRewriter Instance =
            new SinglePointSearchParameterRewriter(new SinglePointSearchParameterRewritePolicy(new SinglePointSearchParameterRegistry()));

        private readonly SinglePointSearchParameterRewritePolicy _policy;

        internal SinglePointSearchParameterRewriter(SinglePointSearchParameterRewritePolicy policy)
        {
            _policy = policy;
        }

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            // Only consider date search parameters
            if (expression.Parameter.Type != SearchParamType.Date)
            {
                return expression;
            }

            // Skip composite parameters
            if (expression.Parameter.Component?.Any() == true)
            {
                return expression;
            }

            // Attempt to normalize the AST into a recognized pattern
            if (!TryMatchPattern(expression.Expression, expression.Comparator, out SinglePointRewritePattern pattern, out int? componentIndex, out object value))
            {
                return expression;
            }

            // Consult the policy for the rewrite decision
            return _policy.Decide(expression.Parameter, pattern) switch
            {
                SinglePointRewriteDecision.RewriteToEndDateTimeEquality => new SearchParameterExpression(
                    expression.Parameter,
                    new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, componentIndex, value),
                    expression.Comparator),
                SinglePointRewriteDecision.UseExistingExpression => expression,
                _ => expression,
            };
        }

        /// <summary>
        /// Attempts to normalize the AST expression into a recognized <see cref="SinglePointRewritePattern"/>.
        /// </summary>
        /// <param name="expression">The expression to normalize.</param>
        /// <param name="comparator">The original comparator for the parsed search value, if known.</param>
        /// <param name="pattern">The normalized pattern, if recognized.</param>
        /// <param name="componentIndex">The component index from the recognized pattern.</param>
        /// <param name="value">The value from the recognized pattern.</param>
        /// <returns>True if the expression matched a recognized pattern; false otherwise.</returns>
        private static bool TryMatchPattern(Expression expression, SearchComparator? comparator, out SinglePointRewritePattern pattern, out int? componentIndex, out object value)
        {
            pattern = SinglePointRewritePattern.Unsupported;
            componentIndex = null;
            value = null;

            // Match single-column predicates: GT/GTE on DateTimeEnd, LT/LTE on DateTimeStart
            if (expression is BinaryExpression binary)
            {
                componentIndex = binary.ComponentIndex;
                value = binary.Value;

                pattern = (binary.FieldName, binary.BinaryOperator) switch
                {
                    (FieldName.DateTimeEnd, BinaryOperator.GreaterThan) => SinglePointRewritePattern.GreaterThan,
                    (FieldName.DateTimeEnd, BinaryOperator.GreaterThanOrEqual) => SinglePointRewritePattern.GreaterThanOrEqual,
                    (FieldName.DateTimeStart, BinaryOperator.LessThan) => SinglePointRewritePattern.LessThan,
                    (FieldName.DateTimeStart, BinaryOperator.LessThanOrEqual) => SinglePointRewritePattern.LessThanOrEqual,
                    _ => SinglePointRewritePattern.Unsupported,
                };

                return pattern != SinglePointRewritePattern.Unsupported;
            }

            if (comparator != SearchComparator.Eq)
            {
                return false;
            }

            // Only parser-tagged eq predicates are eligible for the equality rewrite.
            // Approximate queries share the same binary shape, so we rely on comparator intent
            // being preserved on SearchParameterExpression rather than re-inferring semantics here.
            if (expression is MultiaryExpression multiary &&
                multiary.MultiaryOperation == MultiaryOperator.And &&
                multiary.Expressions.Count == 2 &&
                multiary.Expressions[0] is BinaryExpression first &&
                multiary.Expressions[1] is BinaryExpression second &&
                first.ComponentIndex == second.ComponentIndex &&
                first.FieldName == FieldName.DateTimeStart &&
                first.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                second.FieldName == FieldName.DateTimeEnd &&
                second.BinaryOperator == BinaryOperator.LessThanOrEqual)
            {
                pattern = SinglePointRewritePattern.Equality;
                componentIndex = first.ComponentIndex;
                value = second.Value;
                return true;
            }

            if (expression is MultiaryExpression reversed &&
                reversed.MultiaryOperation == MultiaryOperator.And &&
                reversed.Expressions.Count == 2 &&
                reversed.Expressions[0] is BinaryExpression left &&
                reversed.Expressions[1] is BinaryExpression right &&
                left.ComponentIndex == right.ComponentIndex &&
                left.FieldName == FieldName.DateTimeEnd &&
                left.BinaryOperator == BinaryOperator.LessThanOrEqual &&
                right.FieldName == FieldName.DateTimeStart &&
                right.BinaryOperator == BinaryOperator.GreaterThanOrEqual)
            {
                pattern = SinglePointRewritePattern.Equality;
                componentIndex = left.ComponentIndex;
                value = left.Value;
                return true;
            }

            return false;
        }
    }
}
