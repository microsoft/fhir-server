// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Rewrites (And (FieldGreaterThanOrEqual DateTimeStart x) (FieldLessThanOrEqual DateTimeEnd y)) to
    /// (And (FieldGreaterThanOrEqual DateTimeStart x) (FieldLessThanOrEqual DateTimeEnd y) (FieldLessThanOrEqual DateTimeStart y)).
    /// It looks specifically for this pattern because it is what we emit in the core layer for an equals search over a date parameter.
    /// This rewriting constrains the range scan over the index (DateTimeStart, DateTimeEnd).
    /// </summary>
    internal class DateTimeEqualityRewriter : ExpressionRewriterWithInitialContext<object>
    {
        internal static readonly DateTimeEqualityRewriter Instance = new();

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            if (expression.Parameter.Type == SearchParamType.Date ||
                (expression.Parameter.Type == SearchParamType.Composite && expression.Parameter.Component.Any(c => c.ResolvedSearchParameter.Type == SearchParamType.Date)))
            {
                return base.VisitSearchParameter(expression, context);
            }

            return expression;
        }

        public override Expression VisitMultiary(MultiaryExpression expression, object context)
        {
            expression = (MultiaryExpression)base.VisitMultiary(expression, context);
            if (expression.MultiaryOperation != MultiaryOperator.And)
            {
                return expression;
            }

            List<Expression> newExpressions = null;
            int i = 0;
            for (; i < expression.Expressions.Count - 1; i++)
            {
                switch (MatchPattern(expression.Expressions[i], expression.Expressions[i + 1]))
                {
                    case ({ } low, { } high):
                        EnsureAllocatedAndPopulated(ref newExpressions, expression.Expressions, i);

                        newExpressions.Add(low);
                        newExpressions.Add(new BinaryExpression(high.BinaryOperator, low.FieldName, high.ComponentIndex, high.Value));
                        newExpressions.Add(high);

                        i++;
                        break;
                    default:
                        newExpressions?.Add(expression.Expressions[i]);
                        break;
                }
            }

            if (newExpressions != null && i < expression.Expressions.Count)
            {
                // add the last entry unless it was matched as a pattern above
                newExpressions.Add(expression.Expressions[^1]);
            }

            return newExpressions == null ? expression : Expression.And(newExpressions);
        }

        private static (BinaryExpression low, BinaryExpression high) MatchPattern(Expression e1, Expression e2)
        {
            if (e1 is not BinaryExpression b1 || e2 is not BinaryExpression b2 || b1.ComponentIndex != b2.ComponentIndex)
            {
                return default;
            }

            if (b1 is { FieldName: FieldName.DateTimeStart, BinaryOperator: BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual } &&
                b2 is { FieldName: FieldName.DateTimeEnd, BinaryOperator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual })
            {
                return (b1, b2);
            }

            if (b2 is { FieldName: FieldName.DateTimeStart, BinaryOperator: BinaryOperator.GreaterThan or BinaryOperator.GreaterThanOrEqual } &&
                b1 is { FieldName: FieldName.DateTimeEnd, BinaryOperator: BinaryOperator.LessThan or BinaryOperator.LessThanOrEqual })
            {
                return (b2, b1);
            }

            return default;
        }
    }
}
