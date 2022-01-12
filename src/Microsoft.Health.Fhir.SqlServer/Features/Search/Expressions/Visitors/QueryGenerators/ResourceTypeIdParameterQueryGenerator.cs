// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceTypeIdParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly ResourceTypeIdParameterQueryGenerator Instance = new ResourceTypeIdParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            if (!context.Model.TryGetResourceTypeId(expression.Value, out var resourceTypeId))
            {
                context.StringBuilder.Append("0 = 1");
                return context;
            }

            return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.Resource.ResourceTypeId, expression.ComponentIndex, resourceTypeId);
        }

        public override SearchParameterQueryGeneratorContext VisitIn(InExpression inExpression, SearchParameterQueryGeneratorContext context)
        {
            var resolvedTypes = new List<object>();

            foreach (var type in inExpression.Values)
            {
                if (!context.Model.TryGetResourceTypeId(type, out var resourceTypeId))
                {
                    return context;
                }

                resolvedTypes.Add(resourceTypeId);
            }

            return VisitSimpleIn(context, VLatest.Resource.ResourceTypeId, resolvedTypes.ToArray());
        }
    }
}
