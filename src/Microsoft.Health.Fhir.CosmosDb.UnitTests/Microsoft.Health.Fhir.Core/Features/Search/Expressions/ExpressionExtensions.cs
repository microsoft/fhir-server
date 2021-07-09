// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public static class ExpressionExtensions
    {
        /// <summary>
        /// Calls <see cref="Expression.AcceptVisitor{TContext,TOutput}"/> using
        /// <see cref="IExpressionVisitorWithInitialContext{TContext,TOutput}.InitialContext"/>
        /// as with the context argument
        /// </summary>
        /// <typeparam name="TContext">The type of the context parameter</typeparam>
        /// <typeparam name="TOutput">The return type of the visitor</typeparam>
        /// <param name="expression">The expression</param>
        /// <param name="visitor">The visitor</param>
        /// <returns>The output from the visit.</returns>
        public static TOutput AcceptVisitor<TContext, TOutput>(this Expression expression,  IExpressionVisitorWithInitialContext<TContext, TOutput> visitor)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            return expression.AcceptVisitor(visitor, visitor.InitialContext);
        }
    }
}
