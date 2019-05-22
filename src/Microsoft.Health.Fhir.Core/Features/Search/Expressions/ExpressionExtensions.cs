// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public static class ExpressionExtensions
    {
        public static TOutput AcceptVisitor<TContext, TOutput>(this IExpression expression,  IExpressionVisitorWithInitialContext<TContext, TOutput> visitor)
        {
            EnsureArg.IsNotNull(expression, nameof(expression));
            EnsureArg.IsNotNull(visitor, nameof(visitor));
            return expression.AcceptVisitor(visitor, visitor.InitialContext);
        }
    }
}
