// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class DenormalizedSearchParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public override SqlQueryGenerator VisitSearchParameter(SearchParameterExpression expression, SqlQueryGenerator context)
        {
            return expression.Expression.AcceptVisitor(this, context);
        }

        public override SqlQueryGenerator VisitMissingSearchParameter(MissingSearchParameterExpression expression, SqlQueryGenerator context)
        {
            context.StringBuilder.Append(expression.IsMissing ? " 1 = 0 " : " 1 = 1 ");
            return context;
        }
    }
}