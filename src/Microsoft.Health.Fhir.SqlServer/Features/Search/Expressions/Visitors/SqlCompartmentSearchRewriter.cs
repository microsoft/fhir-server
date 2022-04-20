// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// SQL compatibility wrapper for CompartmentSearchExpression.
    /// </summary>
    internal sealed class SqlCompartmentSearchRewriter : SqlExpressionRewriterWithInitialContext<object>
    {
        private readonly CompartmentSearchRewriter _compartmentSearchRewriter;

        public SqlCompartmentSearchRewriter(CompartmentSearchRewriter compartmentSearchRewriter)
        {
            _compartmentSearchRewriter = EnsureArg.IsNotNull(compartmentSearchRewriter, nameof(compartmentSearchRewriter));
        }

        public override Expression VisitCompartment(CompartmentSearchExpression expression, object context)
        {
            Expression finalExpression = _compartmentSearchRewriter.VisitCompartment(expression, context);

            return finalExpression;
        }
    }
}
