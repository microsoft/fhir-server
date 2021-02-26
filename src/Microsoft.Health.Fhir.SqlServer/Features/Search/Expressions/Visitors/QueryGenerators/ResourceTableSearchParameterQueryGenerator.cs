// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    /// <summary>
    /// A base class for <see cref="SearchParameterQueryGenerator"/>s that are for search parameters on the Resource table.
    /// </summary>
    internal class ResourceTableSearchParameterQueryGenerator : SearchParameterQueryGenerator
    {
        /// <summary>
        /// This instance is intended to be used for expressions that exclusively over search parameters on the Resource table or the Resource table and Search parameter tables
        /// </summary>
        public static readonly ResourceTableSearchParameterQueryGenerator Instance = new ResourceTableSearchParameterQueryGenerator();

        public override SearchParameterQueryGeneratorContext VisitSearchParameter(SearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return expression.Expression.AcceptVisitor(GetSearchParameterQueryGenerator(expression), context);
        }

        public override SearchParameterQueryGeneratorContext VisitMissingSearchParameter(MissingSearchParameterExpression expression, SearchParameterQueryGeneratorContext context)
        {
            // Call this method but discard the result to ensure the search parameter is one we are expecting.
            GetSearchParameterQueryGenerator(expression);

            context.StringBuilder.Append(expression.IsMissing ? " 1 = 0 " : " 1 = 1 ");
            return context;
        }

        private SearchParameterQueryGenerator GetSearchParameterQueryGenerator(SearchParameterExpressionBase searchParameter)
        {
            return GetSearchParameterQueryGeneratorIfResourceColumnSearchParameter(searchParameter) ?? throw new InvalidOperationException($"Unexpected search parameter {searchParameter.Parameter.Code}");
        }
    }
}
