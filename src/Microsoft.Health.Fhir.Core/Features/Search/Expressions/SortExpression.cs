// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a sort parameter expression.
    /// </summary>
    public class SortExpression : SearchParameterExpressionBase
    {
        public SortExpression(SearchParameterInfo searchParameter)
            : base(searchParameter)
        {
        }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSortParameter(this, context);
        }

        public override string ToString()
        {
            return $"(Sort Param: {Parameter.Code})";
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(SortExpression));
            hashCode.Add(Parameter);
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is SortExpression sortExpression && sortExpression.Parameter.Equals(Parameter);
        }
    }
}
