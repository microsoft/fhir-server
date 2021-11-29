// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search.Expressions
{
    public interface ICosmosExpressionVisitor<in TContext, out TOutput> : IExpressionVisitor<TContext, TOutput>
    {
        /// <summary>
        /// Visits the <see cref="InExpression"/>.
        /// </summary>
        /// <param name="expression">The expression to visit.</param>
        /// <param name="context">The input</param>
        TOutput VisitIn(InExpression expression, TContext context);
    }
}
