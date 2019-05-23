// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    /// <summary>
    /// An <see cref="IExpressionVisitor{TContext,TOutput}"/> that provides an initial <typeparamref name="TContext"/> value
    /// to be passed in to <see cref="Expression.AcceptVisitor{TContext,TOutput}"/>. Intended to be used with
    /// <see cref="ExpressionExtensions.AcceptVisitor{TContext,TOutput}"/>.
    /// </summary>
    /// <typeparam name="TContext">The type of the context parameter passed into each Visit method</typeparam>
    /// <typeparam name="TOutput">The type returned by the Visit methods</typeparam>
    public interface IExpressionVisitorWithInitialContext<TContext, out TOutput> : IExpressionVisitor<TContext, TOutput>
    {
        /// <summary>
        /// The initial context to be passed in to <see cref="Expression.AcceptVisitor{TContext,TOutput}"/>.
        /// </summary>
        TContext InitialContext { get; }
    }
}
