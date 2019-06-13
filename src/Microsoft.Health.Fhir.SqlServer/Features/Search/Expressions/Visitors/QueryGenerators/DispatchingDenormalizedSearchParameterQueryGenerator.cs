// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class DispatchingDenormalizedSearchParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public static readonly DispatchingDenormalizedSearchParameterQueryGenerator Instance = new DispatchingDenormalizedSearchParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitSearchParameter(SearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.Expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), context);
        }

        public override SearchParameterQueryGeneratorContext VisitMissingSearchParameter(MissingSearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), context);
        }

        private SearchParameterQueryGenerator GetSearchParameterQueryGenerator(SearchParameterExpressionBase searchParameter)
        {
            switch (searchParameter.Parameter.Name)
            {
                case SearchParameterNames.Id:
                    return ResourceIdParameterQueryGenerator.Instance;
                case SearchParameterNames.ResourceType:
                    return ResourceTypeIdParameterQueryGenerator.Instance;
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                    return ResourceSurrogateIdParameterQueryGenerator.Instance;
                default:
                    throw new NotSupportedException(searchParameter.Parameter.Name);
            }
        }
    }
}
