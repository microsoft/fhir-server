// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class UriSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly UriSearchParameterQueryGenerator Instance = new UriSearchParameterQueryGenerator();

        public override Table Table => V1.UriSearchParam;

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            return VisitSimpleString(expression, context, V1.UriSearchParam.Uri, expression.Value);
        }
    }
}
