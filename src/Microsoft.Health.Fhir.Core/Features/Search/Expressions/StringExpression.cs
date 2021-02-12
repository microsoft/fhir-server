// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an string expression.
    /// </summary>
    public class StringExpression : Expression, IFieldExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StringExpression"/> class.
        /// </summary>
        /// <param name="stringOperator">The string operator.</param>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        /// <param name="value">The value.</param>
        /// <param name="ignoreCase">A flag indicating whether it's case and accent sensitive or not.</param>
        public StringExpression(StringOperator stringOperator, FieldName fieldName, int? componentIndex, string value, bool ignoreCase)
        {
            StringOperator = stringOperator;
            FieldName = fieldName;
            ComponentIndex = componentIndex;
            Value = value;
            IgnoreCase = ignoreCase;
        }

        /// <summary>
        /// Gets the string operator.
        /// </summary>
        public StringOperator StringOperator { get; }

        /// <inheritdoc />
        public FieldName FieldName { get; }

        /// <inheritdoc />
        public int? ComponentIndex { get; }

        /// <summary>
        /// Gets the value.
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Gets a value indicating whether it's case and accent sensitive or not.
        /// </summary>
        public bool IgnoreCase { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitString(this, context);
        }

        public override string ToString()
        {
            return $"(String{StringOperator}{(IgnoreCase ? "IgnoreCase" : null)} {(ComponentIndex == null ? null : $"[{ComponentIndex}].")}{FieldName} '{Value}')";
        }
    }
}
