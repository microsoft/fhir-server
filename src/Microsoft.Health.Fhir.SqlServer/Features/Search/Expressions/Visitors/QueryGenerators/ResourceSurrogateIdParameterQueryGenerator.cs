// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceSurrogateIdParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly ResourceSurrogateIdParameterQueryGenerator Instance = new ResourceSurrogateIdParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            VisitSimpleBinary(expression.BinaryOperator, context, context.ResourceSurrogateIdColumn, expression.ComponentIndex, expression.Value, includeInParameterHash: false);
            return context;
        }
    }
}
