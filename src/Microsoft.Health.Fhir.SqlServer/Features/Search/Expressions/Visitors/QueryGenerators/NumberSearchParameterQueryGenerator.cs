// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class NumberSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly NumberSearchParameterQueryGenerator Instance = new NumberSearchParameterQueryGenerator();

        public override Table Table => V1.NumberSearchParam;

        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            var column = V1.NumberSearchParam.SingleValue;
            context.StringBuilder.Append(column).Append(expression.ComponentIndex + 1).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, column, expression.ComponentIndex, expression.Value);
        }
    }
}