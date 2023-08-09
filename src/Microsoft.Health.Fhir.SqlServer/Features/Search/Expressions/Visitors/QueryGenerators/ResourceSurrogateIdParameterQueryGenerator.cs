// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceSurrogateIdParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly ResourceSurrogateIdParameterQueryGenerator Instance = new ResourceSurrogateIdParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            VisitSimpleBinary(expression.BinaryOperator, context, VLatest.ResourceCurrent.ResourceSurrogateId, expression.ComponentIndex, expression.Value, includeInParameterHash: false);
            return context;
        }
    }
}
