// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public class CompositeComponentSearchParameterExpression : SearchParameterExpression
    {
        public CompositeComponentSearchParameterExpression(SearchParameter searchParameter, Expression expression, int componentIndex)
            : base(searchParameter, expression)
        {
            ComponentIndex = componentIndex;
        }

        public int ComponentIndex { get; }

        protected internal override void AcceptVisitor(IExpressionVisitor visitor)
        {
            visitor.Visit(this);
        }
    }
}
