// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents an 'in' expression where known values are grouped together.
    /// </summary>
    /// <typeparam name="T">Type of the value included in the expression.</typeparam>
    public class InExpression<T> : Expression, IFieldExpression
    {
        public InExpression(FieldName fieldName, int? componentIndex, IEnumerable<T> values)
            : this(fieldName, componentIndex)
        {
            Values = EnsureArg.HasItems(values?.ToArray(), nameof(values));
        }

        public InExpression(FieldName fieldName, int? componentIndex, IReadOnlyList<T> values)
            : this(fieldName, componentIndex)
        {
            Values = EnsureArg.HasItems(values, nameof(values));
        }

        private InExpression(FieldName fieldName, int? componentIndex)
        {
            FieldName = fieldName;
            ComponentIndex = componentIndex;
        }

        public FieldName FieldName { get; }

        public int? ComponentIndex { get; }

        public IReadOnlyList<T> Values { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return visitor.VisitIn(this, context);
        }

        public override string ToString()
        {
            return $"({(ComponentIndex == null ? null : $"[{ComponentIndex}].")}{FieldName} IN ({string.Join(", ", Values)}))";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(InExpression<T>));
            hashCode.Add(FieldName);
            hashCode.Add(ComponentIndex);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is InExpression<T> expression &&
                   expression.FieldName == FieldName &&
                   expression.ComponentIndex == ComponentIndex;
        }
    }
}
