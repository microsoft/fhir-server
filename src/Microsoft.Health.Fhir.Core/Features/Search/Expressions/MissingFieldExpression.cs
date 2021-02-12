// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an expression that indicates the field should be missing.
    /// </summary>
    public class MissingFieldExpression : Expression, IFieldExpression
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MissingFieldExpression"/> class.
        /// </summary>
        /// <param name="fieldName">The field name.</param>
        /// <param name="componentIndex">The component index.</param>
        public MissingFieldExpression(FieldName fieldName, int? componentIndex)
        {
            FieldName = fieldName;
            ComponentIndex = componentIndex;
        }

        /// <inheritdoc />
        public FieldName FieldName { get; }

        /// <inheritdoc />
        public int? ComponentIndex { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return visitor.VisitMissingField(this, context);
        }

        public override string ToString()
        {
            return $"(MissingField {(ComponentIndex == null ? null : $"[{ComponentIndex}].")}{FieldName})";
        }
    }
}
