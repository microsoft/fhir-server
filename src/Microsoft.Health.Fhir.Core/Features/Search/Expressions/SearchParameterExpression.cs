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
    public class SearchParameterExpression : SearchParameterExpressionBase
    {
        public SearchParameterExpression(SearchParameterInfo searchParameter, Expression expression)
            : base(searchParameter)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));

            Expression = expression;
        }

        public Expression Expression { get; }

        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return visitor.VisitSearchParameter(this, context);
        }

        public override string ToString()
        {
            return $"(Param {Parameter.Name} {Expression})";
        }
    }
}
