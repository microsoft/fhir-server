// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// For expressions over a reference search parameter where the type is not specified
    /// (e.g. "abc" instead of "Patient/123") AND the search parameter is constrained to be
    /// of only one type, rewrites the expression to specify the single target type.
    /// </summary>
    internal class UntypedReferenceRewriter : ExpressionRewriterWithInitialContext<object>
    {
        public static readonly UntypedReferenceRewriter Instance = new UntypedReferenceRewriter();

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            // Handle reference search parameters as well as composite search parameters with one or more reference components.
            // We first create a bitmask with bits set representing the component indexes that are candidates for this rule.
            // Bit 0 is for reference, non-composite parameters where the reference is of a single possible type
            // Bits 1 and up represent the component indexes (plus one) where the component is a reference search parameter with one type

            int componentCandidates = expression.Parameter.Type switch
            {
                SearchParamType.Reference when expression.Parameter.TargetResourceTypes?.Count == 1 => 1,
                SearchParamType.Composite =>
                    expression.Parameter.Component.Aggregate(
                            (index: 1, flags: 0),
                            (acc, c) => (index: acc.index + 1, flags: acc.flags | (c.ResolvedSearchParameter.Type == SearchParamType.Reference && c.ResolvedSearchParameter.TargetResourceTypes?.Count == 1 ? 1 << acc.index : 0)))
                        .flags,
                _ => 0,
            };

            if (componentCandidates == 0)
            {
                // nothing to do for this expression
                return expression;
            }

            if (expression.Expression is MultiaryExpression multiaryExpression && multiaryExpression.MultiaryOperation == MultiaryOperator.Or)
            {
                Expression[] rewrittenExpressions = null;
                for (var i = 0; i < multiaryExpression.Expressions.Count; i++)
                {
                    Expression subexpression = multiaryExpression.Expressions[i];
                    Expression rewrittenSubexpression = RewriteSubexpression(expression.Parameter, subexpression, componentCandidates);

                    if (!ReferenceEquals(rewrittenSubexpression, subexpression))
                    {
                        EnsureAllocatedAndPopulated(ref rewrittenExpressions, multiaryExpression.Expressions, i);
                    }

                    if (rewrittenExpressions != null)
                    {
                        rewrittenExpressions[i] = rewrittenSubexpression;
                    }
                }

                if (rewrittenExpressions == null)
                {
                    return expression;
                }

                return Expression.SearchParameter(expression.Parameter, Expression.Or(rewrittenExpressions));
            }

            // a single expression (possibly ANDs), not multiple expressions ORed together

            Expression rewrittenExpression = RewriteSubexpression(expression.Parameter, expression.Expression, componentCandidates);

            if (ReferenceEquals(rewrittenExpression, expression.Expression))
            {
                return expression;
            }

            return Expression.SearchParameter(expression.Parameter, rewrittenExpression);
        }

        /// <summary>
        /// Attempts to rewrite a expression adding in a reference type predicate where missing and possible.
        /// Can be called for individual operands of an OR expression.
        /// </summary>
        /// <param name="searchParameter">The context search parameter</param>
        /// <param name="expression">The expression to rewrite</param>
        /// <param name="componentCandidates">
        ///     A bitset with bits set at indexes where components are reference search parameters that can be of a single type.
        ///     Bit index 0 is used for non-composite search parameters. Bits 1 and up are the one-based indexes of the components of a composite search parameter.</param>
        /// <returns>A rewritten expression or the same instance if no changes made.</returns>
        private static Expression RewriteSubexpression(SearchParameterInfo searchParameter, Expression expression, int componentCandidates)
        {
            // now see which components have a expression on the reference type
            int componentsPresent = expression.AcceptVisitor(ParameterPredicateVisitor.Instance, null);

            // Now determine which components should get a type expression added to it.
            // componentCandidates will have bits set for each component that we could provide a known type expression for.
            // componentsPresent will have bits set for each component that actually has a type expression.
            // So the components that we need to provide a type expression for can be obtained by a set difference,
            // which we can do with a bitwise complement (~) and bitwise intersection (&).

            int componentsToFill = componentCandidates & ~componentsPresent;

            if (componentsToFill == 0)
            {
                return expression;
            }

            List<Expression> newExpressionsToBeAnded;
            if (expression is MultiaryExpression me && me.MultiaryOperation == MultiaryOperator.And)
            {
                newExpressionsToBeAnded = new List<Expression>(me.Expressions.Count + 1);
                newExpressionsToBeAnded.AddRange(me.Expressions);
            }
            else
            {
                newExpressionsToBeAnded = new List<Expression>(1) { expression };
            }

            // Now go through each bit set on componentsToFill.
            // For each of those, add in a type expression

            for (int i = 0, x = componentsToFill; x != 0; i++, x >>= 1)
            {
                if ((x & 1) == 0)
                {
                    continue;
                }

                int? actualComponentIndex = i == 0 ? null : (int?)(i - 1);

                string targetResourceType = actualComponentIndex == null
                    ? searchParameter.TargetResourceTypes.Single()
                    : searchParameter.Component[actualComponentIndex.Value].ResolvedSearchParameter.TargetResourceTypes.Single();

                newExpressionsToBeAnded.Add(Expression.StringEquals(FieldName.ReferenceResourceType, actualComponentIndex, targetResourceType, false));
            }

            return Expression.And(newExpressionsToBeAnded);
        }

        private class ParameterPredicateVisitor : DefaultExpressionVisitor<object, int>
        {
            internal static readonly ParameterPredicateVisitor Instance = new ParameterPredicateVisitor();

            private ParameterPredicateVisitor()
                : base((acc, curr) => acc | curr)
            {
            }

            public override int VisitMultiary(MultiaryExpression expression, object context)
            {
                if (expression.MultiaryOperation != MultiaryOperator.And)
                {
                    throw new InvalidOperationException($"Unexpected {nameof(MultiaryExpression)}.{expression.MultiaryOperation}");
                }

                return base.VisitMultiary(expression, context);
            }

            public override int VisitString(StringExpression expression, object context)
            {
                return expression.FieldName == FieldName.ReferenceResourceType
                    ? 1 << (expression.ComponentIndex + 1 ?? 0)
                    : 0;
            }
        }
    }
}
