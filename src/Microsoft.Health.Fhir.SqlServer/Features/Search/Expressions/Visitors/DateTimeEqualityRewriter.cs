// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewrites (And (FieldGreaterThanOrEqual DateTimeStart x) (FieldLessThanOrEqual DateTimeEnd y)) to
    /// (And (FieldGreaterThanOrEqual DateTimeStart x) (FieldLessThanOrEqual DateTimeEnd y) (FieldLessThanOrEqual DateTimeStart y)).
    /// It looks specifically for this pattern because it is what we emit in the core layer for an equals search over a date parameter.
    /// This rewriting constrains the range scan over the index (DateTimeStart, DateTimeEnd).
    /// </summary>
    internal class DateTimeEqualityRewriter : ExpressionRewriterWithInitialContext<object>
    {
        internal static readonly DateTimeEqualityRewriter Instance = new DateTimeEqualityRewriter();

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            // include composites because they may contain dates.

            if (expression.Parameter.Type == SearchParamType.Date ||
                expression.Parameter.Type == SearchParamType.Composite)
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
            for (int i = 0; i < expression.Expressions.Count - 1; i++)
            {
                if (expression.Expressions[i] is BinaryExpression left &&
                    left.FieldName == FieldName.DateTimeStart &&
                    left.BinaryOperator == BinaryOperator.GreaterThanOrEqual &&
                    expression.Expressions[i + 1] is BinaryExpression right &&
                    right.FieldName == FieldName.DateTimeEnd &&
                    right.BinaryOperator == BinaryOperator.LessThanOrEqual &&
                    left.ComponentIndex == right.ComponentIndex)
                {
                    EnsureAllocatedAndPopulated(ref newExpressions, expression.Expressions, i);

                    newExpressions.Add(left);
                    newExpressions.Add(Expression.LessThanOrEqual(left.FieldName, right.ComponentIndex, right.Value));
                    newExpressions.Add(right);

                    i++;
                }
                else if (newExpressions != null)
                {
                    newExpressions.Add(expression.Expressions[i]);
                    if (i == expression.Expressions.Count - 2)
                    {
                        newExpressions.Add(expression.Expressions[i + 1]);
                    }
                }
            }

            if (newExpressions == null)
            {
                return expression;
            }

            return Expression.And(newExpressions);
        }
    }
}
