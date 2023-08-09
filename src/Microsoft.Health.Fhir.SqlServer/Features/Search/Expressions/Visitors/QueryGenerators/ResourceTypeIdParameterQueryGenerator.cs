// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

            return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.ResourceCurrent.ResourceTypeId, expression.ComponentIndex, resourceTypeId);
        }

        public override SearchParameterQueryGeneratorContext VisitIn<T>(InExpression<T> expression, SearchParameterQueryGeneratorContext context)
        {
            List<short> resolvedResourceTypeIds = new List<short>(capacity: expression.Values.Count);
            foreach (T resourceType in expression.Values)
            {
                string resourceTypeName = resourceType.ToString();
                if (context.Model.TryGetResourceTypeId(resourceTypeName, out short resourceTypeId))
                {
                    resolvedResourceTypeIds.Add(resourceTypeId);
                }
                else
                {
                    throw new InvalidOperationException(string.Format(Resources.InvalidResourceTypeValue, resourceType));
                }
            }

            return VisitSimpleIn(context, VLatest.ResourceCurrent.ResourceTypeId, resolvedResourceTypeIds);
        }
    }
}
