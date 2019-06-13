// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal abstract class DenormalizedSearchParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public override SearchParameterQueryGeneratorContext VisitSearchParameter(SearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.Expression.AcceptVisitor(this, context);
        }

        public override SearchParameterQueryGeneratorContext VisitMissingSearchParameter(MissingSearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            context.StringBuilder.Append(expression.IsMissing ? " 1 = 0 " : " 1 = 1 ");
            return context;
        }
    }
}
