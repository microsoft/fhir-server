// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class StringSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly StringSearchParameterQueryGenerator Instance = new StringSearchParameterQueryGenerator();

        public override Table Table => V1.StringSearchParam;

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            context.StringBuilder.Append(V1.StringSearchParam.TextOverflow).Append(expression.ComponentIndex + 1).Append(" IS NULL AND ");
            return VisitSimpleString(expression, context, V1.StringSearchParam.Text, expression.Value);
        }
    }
}