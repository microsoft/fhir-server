// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a set of ANDed expressions over a search parameter.
    /// </summary>
    public class SortParameterExpression : SearchParameterExpressionBase
    {
        public SortParameterExpression(SearchParameterInfo searchParameter)
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
            return $"(Param {Parameter.Name})";
        }
    }
}
