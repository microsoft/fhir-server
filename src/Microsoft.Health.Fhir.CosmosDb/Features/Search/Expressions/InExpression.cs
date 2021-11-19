// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Expressions
{
    /// <summary>
    /// In CosmosDB, allows for use of ARRAY_CONTAINS to group known values instead of multiple ORs.
    /// </summary>
    public class InExpression : Expression, IFieldExpression
    {
        public InExpression(FieldName fieldName, int? componentIndex, IEnumerable<string> values)
        {
            FieldName = fieldName;
            ComponentIndex = componentIndex;
            Values = EnsureArg.IsNotNull(values, nameof(values));
        }

        public FieldName FieldName { get; }

        public int? ComponentIndex { get; }

        public IEnumerable<string> Values { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            return ((ICosmosExpressionVisitor<TContext, TOutput>)visitor).VisitIn(this, context);
        }

        public override string ToString()
        {
            return $"({(ComponentIndex == null ? null : $"[{ComponentIndex}].")}{FieldName} IN ({string.Join(", ", Values)}))";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(InExpression));
            hashCode.Add(FieldName);
            hashCode.Add(ComponentIndex);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is InExpression expression &&
                   expression.FieldName == FieldName &&
                   expression.ComponentIndex == ComponentIndex;
        }
    }
}
