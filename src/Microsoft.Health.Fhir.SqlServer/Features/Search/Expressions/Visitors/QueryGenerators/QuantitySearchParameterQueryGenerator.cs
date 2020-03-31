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
    internal class QuantitySearchParameterQueryGenerator : NormalizedSearchParameterQueryGenerator
    {
        public static readonly QuantitySearchParameterQueryGenerator Instance = new QuantitySearchParameterQueryGenerator();

        public override Table Table => VLatest.QuantitySearchParam;

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
        {
            NullableDecimalColumn valueColumn;
            NullableDecimalColumn nullCheckColumn;
            switch (expression.FieldName)
            {
                case FieldName.Quantity:
                    valueColumn = nullCheckColumn = VLatest.QuantitySearchParam.SingleValue;
                    break;
                case SqlFieldName.QuantityLow:
                    valueColumn = nullCheckColumn = VLatest.QuantitySearchParam.LowValue;
                    break;
                case SqlFieldName.QuantityHigh:
                    valueColumn = VLatest.QuantitySearchParam.HighValue;
                    nullCheckColumn = VLatest.QuantitySearchParam.LowValue;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }

            AppendColumnName(context, nullCheckColumn, expression).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
        }

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.QuantityCode:
                    if (context.Model.TryGetQuantityCodeId(expression.Value, out var quantityCodeId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.QuantitySearchParam.QuantityCodeId, expression.ComponentIndex, quantityCodeId);
                    }

                    AppendColumnName(context, VLatest.QuantitySearchParam.QuantityCodeId, expression)
                        .Append(" IN (SELECT ")
                        .Append(VLatest.QuantityCode.QuantityCodeId, null)
                        .Append(" FROM ").Append(VLatest.QuantityCode)
                        .Append(" WHERE ")
                        .Append(VLatest.QuantityCode.Value, null)
                        .Append(" = ")
                        .Append(context.Parameters.AddParameter(VLatest.QuantityCode.Value, expression.Value))
                        .Append(")");

                    return context;

                case FieldName.QuantitySystem:
                    if (context.Model.TryGetSystemId(expression.Value, out var systemId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, VLatest.QuantitySearchParam.SystemId, expression.ComponentIndex, systemId);
                    }

                    AppendColumnName(context, VLatest.QuantitySearchParam.SystemId, expression)
                        .Append(" IN (SELECT ")
                        .Append(VLatest.System.SystemId, null)
                        .Append(" FROM ").Append(VLatest.System)
                        .Append(" WHERE ")
                        .Append(VLatest.System.Value, null)
                        .Append(" = ")
                        .Append(context.Parameters.AddParameter(VLatest.System.Value, expression.Value))
                        .Append(")");

                    return context;

                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }
    }
}
