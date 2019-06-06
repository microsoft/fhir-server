// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    internal abstract class SqlExpressionRewriterWithInitialContext<TContext> : SqlExpressionRewriter<TContext>, IExpressionVisitorWithInitialContext<TContext, Expression>
    {
        public virtual TContext InitialContext => default;
    }
}
