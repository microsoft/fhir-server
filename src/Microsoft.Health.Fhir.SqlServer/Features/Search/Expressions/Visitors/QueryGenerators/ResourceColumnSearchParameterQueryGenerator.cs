// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class ResourceColumnSearchParameterQueryGenerator : SearchParameterQueryGenerator
    {
        public static readonly ResourceColumnSearchParameterQueryGenerator Instance = new ResourceColumnSearchParameterQueryGenerator();

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
            return GetSearchParameterQueryGeneratorIfResourceColumnSearchParameter(searchParameter) ?? throw new InvalidOperationException($"Unexpected search parameter {searchParameter.Parameter.Name}");
        }
    }
}
