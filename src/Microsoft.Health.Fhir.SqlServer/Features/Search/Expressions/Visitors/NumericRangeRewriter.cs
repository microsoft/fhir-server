// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Rewrites expressions over Quantity and Number values to take ranges into account.
    /// They will combine the results of the original expression with the results of the
    /// expressions over the low and high fields where the entry is not a single value.
    /// </summary>
    internal class NumericRangeRewriter : ConcatenationRewriter
    {
        internal static readonly NumericRangeRewriter Instance = new NumericRangeRewriter();

        private NumericRangeRewriter()
            : base(new Scout())
        {
        }

        public override Expression VisitBinary(BinaryExpression expression, object context)
        {
            FieldName highField;
            FieldName lowField;
            switch (expression.FieldName)
            {
                case FieldName.Quantity:
                    highField = SqlFieldName.QuantityHigh;
                    lowField = SqlFieldName.QuantityLow;
                    break;
                case FieldName.Number:
                    highField = SqlFieldName.NumberHigh;
                    lowField = SqlFieldName.NumberLow;
                    break;
                default:
                    return expression;
            }

            switch (expression.BinaryOperator)
            {
                case BinaryOperator.GreaterThan:
                    return Expression.GreaterThan(highField, expression.ComponentIndex, expression.Value);
                case BinaryOperator.GreaterThanOrEqual:
                    return Expression.GreaterThanOrEqual(highField, expression.ComponentIndex, expression.Value);
                case BinaryOperator.LessThan:
                    return Expression.LessThan(lowField, expression.ComponentIndex, expression.Value);
                case BinaryOperator.LessThanOrEqual:
                    return Expression.LessThanOrEqual(lowField, expression.ComponentIndex, expression.Value);
                case BinaryOperator.Equal:
                case BinaryOperator.NotEqual:
                default:
                    throw new ArgumentOutOfRangeException(expression.BinaryOperator.ToString());
            }
        }

        private class Scout : DefaultExpressionVisitor<object, bool>
        {
            internal Scout()
                : base((accumulated, current) => accumulated || current)
            {
            }

            public override bool VisitBinary(BinaryExpression expression, object context)
            {
                return expression.FieldName == FieldName.Quantity || expression.FieldName == FieldName.Number;
            }
        }
    }
}
