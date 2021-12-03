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
    internal class NumberQueryGenerator : SearchParamTableExpressionQueryGenerator
    {
        public static readonly NumberQueryGenerator Instance = new NumberQueryGenerator();

        public override Table Table => VLatest.NumberSearchParam;

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            NullableDecimalColumn valueColumn = null;
            DecimalColumn notNullableValueColumn = null;

            switch (expression.FieldName)
            {
                case FieldName.Number:
                    valueColumn = VLatest.NumberSearchParam.SingleValue;
                    break;
                case SqlFieldName.NumberLow:
                    notNullableValueColumn = VLatest.NumberSearchParam.LowValue;
                    break;
                case SqlFieldName.NumberHigh:
                    notNullableValueColumn = VLatest.NumberSearchParam.HighValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            if (valueColumn != null)
            {
                AppendColumnName(context, valueColumn, expression).Append(" IS NOT NULL AND ");
                return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
            }

            return VisitSimpleBinary(expression.BinaryOperator, context, notNullableValueColumn, expression.ComponentIndex, expression.Value);
        }
    }
}
