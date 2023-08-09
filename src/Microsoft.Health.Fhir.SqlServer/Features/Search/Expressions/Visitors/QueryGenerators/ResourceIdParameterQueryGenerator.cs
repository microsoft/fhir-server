// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceIdParameterQueryGenerator : ResourceTableSearchParameterQueryGenerator
    {
        public static new readonly ResourceIdParameterQueryGenerator Instance = new ResourceIdParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            VisitSimpleString(expression, context, VLatest.ResourceCurrent.ResourceId, expression.Value);

            return context;
        }
    }
}
