// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewrites (And (FieldGreaterThanOrEqual DateTimeStart x) (FieldLessThanOrEqual DateTimeStart y)) to
    /// (And (FieldGreaterThanOrEqual DateTimeStart x) (FieldLessThanOrEqual DateTimeEnd y) (FieldLessThanOrEqual DateTimeStart y)).
    /// This rewriting constrains the range scan over the index (DateTimeStart, DateTimeEnd).
    /// </summary>
    internal class DateTimeRangeRewriter : ExpressionRewriterWithInitialContext<object>
    {
        internal static readonly DateTimeRangeRewriter Instance = new DateTimeRangeRewriter();

        public override Expression VisitSearchParameter(SearchParameterExpression expression, object context)
        {
            // _lastUpdated is not stored as a range so this transformation does not apply.

            // include composites because they may contain dates.

            if ((expression.Parameter.Type == SearchParamType.Date
                 && expression.Parameter.Name != SearchParameterNames.LastUpdated) ||
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
                    if (newExpressions == null)
                    {
                        newExpressions = new List<Expression>();
                        for (int j = 0; j < i; j++)
                        {
                            newExpressions.Add(expression.Expressions[j]);
                        }
                    }

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
