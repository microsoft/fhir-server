// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Expressions
{
    public abstract class ExpressionRewriterWithDefaultInitialContext<TContext> : ExpressionRewriterWithInitialContext<TContext>
    {
        public override TContext InitialContext => default;
    }
}
