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
    /// Currently handles only single-column range predicates (GT/GTE on DateTimeEnd, LT/LTE on DateTimeStart).
    /// Two-sided AND patterns are not rewritten because the parser emits identical AST shapes for both
    /// equality and approximate date searches, making safe distinction impossible at the SQL layer.
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
            if (!TryMatchPattern(expression.Expression, out SinglePointRewritePattern pattern, out int? componentIndex, out object value))
            {
                return expression;
            }

            // Consult the policy for the rewrite decision
            return _policy.Decide(expression.Parameter, pattern) switch
            {
                SinglePointRewriteDecision.RewriteToEndDateTimeEquality => new SearchParameterExpression(
                    expression.Parameter,
                    new BinaryExpression(BinaryOperator.Equal, FieldName.DateTimeEnd, componentIndex, value)),
                SinglePointRewriteDecision.UseExistingExpression => expression,
                _ => expression,
            };
        }

        /// <summary>
        /// Attempts to normalize the AST expression into a recognized <see cref="SinglePointRewritePattern"/>.
        /// </summary>
        /// <param name="expression">The expression to normalize.</param>
        /// <param name="pattern">The normalized pattern, if recognized.</param>
        /// <param name="componentIndex">The component index from the recognized pattern.</param>
        /// <param name="value">The value from the recognized pattern.</param>
        /// <returns>True if the expression matched a recognized pattern; false otherwise.</returns>
        private static bool TryMatchPattern(Expression expression, out SinglePointRewritePattern pattern, out int? componentIndex, out object value)
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

            // Note: Two-sided AND predicates (DateTimeStart >= ... AND DateTimeEnd <= ...) are deliberately NOT matched here.
            // The parser emits the same shape for both equality and approximate date searches, so we cannot safely distinguish
            // them at the SQL layer. The equality rewrite is disabled until a higher-level seam (e.g., from the parser or
            // search service) can provide unambiguous metadata about the search intent (Task 5/6).
            // For now, all multi-clause expressions pass through unchanged.

            return false;
        }
    }
}
