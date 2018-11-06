// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// Represents a set of ANDed expressions over a search parameter.
    /// </summary>
    public class SearchParameterExpression : SearchParameterExpressionBase
    {
        public SearchParameterExpression(SearchParameter searchParameter, Expression expression)
            : base(searchParameter)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));

            Expression = expression;
        }

        public Expression Expression { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            visitor.Visit(this);
        }
    }
}
