// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Walks an expression tree and reports whether it contains any <see cref="ChainedExpression"/>
    /// (forward chained or reverse-chained <c>_has</c>) at any depth, including chains nested inside
    /// <see cref="MultiaryExpression"/>, <see cref="UnionExpression"/>, <see cref="NotExpression"/>,
    /// or <see cref="SearchParameterExpression"/> containers.
    /// </summary>
    internal sealed class ContainsChainedExpressionVisitor : DefaultExpressionVisitor<object, bool>
    {
        internal static readonly ContainsChainedExpressionVisitor Instance = new ContainsChainedExpressionVisitor();

        private ContainsChainedExpressionVisitor()
            : base((accumulated, current) => accumulated || current)
        {
        }

        public override bool VisitChained(ChainedExpression expression, object context) => true;
    }
}
