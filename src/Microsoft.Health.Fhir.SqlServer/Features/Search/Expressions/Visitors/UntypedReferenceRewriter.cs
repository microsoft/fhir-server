﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
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
            // We fist create a bitmask with bits set representing the component indexes that are candidates for this rule.
            // Bit 0 is for reference, non-composite parameters where the reference if of one type
            // Bits 1 and up represent the component indexes (plus one) where the component is a reference search parameter with one type

            int componentCandidates = expression.Parameter.Type switch
            {
                SearchParamType.Reference when expression.Parameter.TargetResourceTypes?.Count == 1 => 1,
                SearchParamType.Composite =>
                    expression.Parameter.ResolvedComponents.Aggregate(
                            (index: 1, flags: 0),
                            (acc, p) => (index: acc.index + 1, flags: acc.flags | (p.Type == SearchParamType.Reference && p.TargetResourceTypes?.Count == 1 ? 1 << acc.index : 0)))
                        .flags,
                _ => 0
            };

            if (componentCandidates == 0)
            {
                // nothing to do for this expression
                return expression;
            }

            // now see which components have a expression on the reference type
            int componentsPresent = expression.Expression.AcceptVisitor(ParameterPredicateVisitor.Instance, null);

            // now determine which ones we get a type expression

            int componentsToFill = componentCandidates & ~componentsPresent;

            if (componentsToFill == 0)
            {
                return expression;
            }

            List<Expression> newExpressionsToBeAnded;
            if (expression.Expression is MultiaryExpression me)
            {
                newExpressionsToBeAnded = new List<Expression>(me.Expressions.Count + 1);
                newExpressionsToBeAnded.AddRange(me.Expressions);
            }
            else
            {
                newExpressionsToBeAnded = new List<Expression>(1) { expression.Expression };
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
                    ? expression.Parameter.TargetResourceTypes.Single()
                    : expression.Parameter.ResolvedComponents[actualComponentIndex.Value].TargetResourceTypes.Single();

                newExpressionsToBeAnded.Add(Expression.StringEquals(FieldName.ReferenceResourceType, actualComponentIndex, targetResourceType, false));
            }

            return new SearchParameterExpression(expression.Parameter, Expression.And(newExpressionsToBeAnded));
        }

        private class ParameterPredicateVisitor : DefaultExpressionVisitor<object, int>
        {
            internal static readonly ParameterPredicateVisitor Instance = new ParameterPredicateVisitor();

            private ParameterPredicateVisitor()
                : base((acc, curr) => acc | curr)
            {
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
