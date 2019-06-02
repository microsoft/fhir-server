// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators
{
    internal class QuantitySearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly QuantitySearchParameterQueryGenerator Instance = new QuantitySearchParameterQueryGenerator();

        public override Table Table => V1.QuantitySearchParam;

        public override SqlQueryGenerator VisitBinary(BinaryExpression expression, SqlQueryGenerator context)
        {
            NullableDecimalColumn valueColumn;
            NullableDecimalColumn nullCheckColumn;
            switch (expression.FieldName)
            {
                case FieldName.Quantity:
                    valueColumn = nullCheckColumn = V1.QuantitySearchParam.SingleValue;
                    break;
                case SqlFieldName.QuantityLow:
                    valueColumn = nullCheckColumn = V1.QuantitySearchParam.LowValue;
                    break;
                case SqlFieldName.QuantityHigh:
                    valueColumn = V1.QuantitySearchParam.HighValue;
                    nullCheckColumn = V1.QuantitySearchParam.LowValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            context.StringBuilder.Append(nullCheckColumn).Append(expression.ComponentIndex + 1).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
        }

        public override SqlQueryGenerator VisitString(StringExpression expression, SqlQueryGenerator context)
        {
            switch (expression.FieldName)
            {
                case FieldName.QuantityCode:
                    return VisitSimpleBinary(BinaryOperator.Equal, context, V1.QuantitySearchParam.QuantityCodeId, expression.ComponentIndex, context.Model.GetQuantityCode(expression.Value));
                case FieldName.QuantitySystem:
                    return VisitSimpleBinary(BinaryOperator.Equal, context, V1.QuantitySearchParam.SystemId, expression.ComponentIndex, context.Model.GetSystem(expression.Value));
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }
    }
}
