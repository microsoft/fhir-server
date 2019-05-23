// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression that has a binary operator.
    /// </summary>
    public class BinaryExpression : Expression, IFieldExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryExpression"/> class.
        /// </summary>
        /// <param name="binaryOperator">The binary operator.</param>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        public BinaryExpression(BinaryOperator binaryOperator, FieldName fieldName, int? componentIndex, object value)
        {
            EnsureArg.IsNotNull(value, nameof(value));

            BinaryOperator = binaryOperator;
            FieldName = fieldName;
            ComponentIndex = componentIndex;
            Value = value;
        }

        /// <summary>
        /// Gets the binary operator.
        /// </summary>
        public BinaryOperator BinaryOperator { get; }

        /// <inheritdoc />
        public FieldName FieldName { get; }

        /// <inheritdoc />
        public int? ComponentIndex { get; }

        /// <summary>
        /// Gets the value.
        /// </summary>
        public object Value { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitBinary(this, context);
        }

        public override string ToString()
        {
            string ValueToString()
            {
                switch (Value)
                {
                    case string _:
                        return $"'{Value}'";
                    case DateTime dt:
                        return dt.ToString("O");
                    case IFormattable f:
                        return f.ToString(null, CultureInfo.InvariantCulture);
                    default:
                        return Value?.ToString();
                }
            }

            return $"(Field{BinaryOperator} {(ComponentIndex == null ? null : $"[{ComponentIndex}].")}{FieldName} {ValueToString()})";
        }
    }
}
