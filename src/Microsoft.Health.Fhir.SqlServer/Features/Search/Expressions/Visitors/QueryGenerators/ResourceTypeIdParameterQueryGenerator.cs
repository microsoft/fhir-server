// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceTypeIdParameterQueryGenerator : DenormalizedSearchParameterQueryGenerator
    {
        public static readonly ResourceTypeIdParameterQueryGenerator Instance = new ResourceTypeIdParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            if (!context.Model.TryGetResourceTypeId(expression.Value, out var resourceTypeId))
            {
                context.StringBuilder.Append("0 = 1");
                return context;
            }

            return VisitSimpleBinary(BinaryOperator.Equal, context, V1.Resource.ResourceTypeId, expression.ComponentIndex, resourceTypeId);
        }
    }
}
