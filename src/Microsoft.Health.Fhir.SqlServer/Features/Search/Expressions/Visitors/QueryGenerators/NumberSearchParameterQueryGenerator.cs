// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class NumberSearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly NumberSearchParameterQueryGenerator Instance = new NumberSearchParameterQueryGenerator();

        public override Table Table => VLatest.NumberSearchParam;

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            NullableDecimalColumn valueColumn;
            NullableDecimalColumn nullCheckColumn;
            switch (expression.FieldName)
            {
                case FieldName.Number:
                    valueColumn = nullCheckColumn = VLatest.NumberSearchParam.SingleValue;
                    break;
                case SqlFieldName.NumberLow:
                    valueColumn = nullCheckColumn = VLatest.NumberSearchParam.LowValue;
                    break;
                case SqlFieldName.NumberHigh:
                    valueColumn = VLatest.NumberSearchParam.HighValue;
                    nullCheckColumn = VLatest.NumberSearchParam.LowValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            AppendColumnName(context, nullCheckColumn, expression).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
        }
    }
}
