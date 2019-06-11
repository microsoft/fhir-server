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

        public override SearchParameterQueryGeneratorContext VisitBinary(BinaryExpression expression, SearchParameterQueryGeneratorContext context)
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

            context.StringBuilder.Append(nullCheckColumn, context.TableAlias).Append(expression.ComponentIndex + 1).Append(" IS NOT NULL AND ");
            return VisitSimpleBinary(expression.BinaryOperator, context, valueColumn, expression.ComponentIndex, expression.Value);
        }

        public override SearchParameterQueryGeneratorContext VisitString(StringExpression expression, SearchParameterQueryGeneratorContext context)
        {
            switch (expression.FieldName)
            {
                case FieldName.QuantityCode:
                    if (context.Model.TryGetQuantityCodeId(expression.Value, out var quantityCodeId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, V1.QuantitySearchParam.QuantityCodeId, expression.ComponentIndex, quantityCodeId);
                    }

                    context.StringBuilder.Append(V1.QuantitySearchParam.QuantityCodeId, context.TableAlias)
                        .Append(" IN (SELECT ")
                        .Append(V1.QuantityCode.QuantityCodeId, null)
                        .Append(" FROM ").Append(V1.QuantityCode)
                        .Append(" WHERE ")
                        .Append(V1.QuantityCode.Value, null)
                        .Append(" = ")
                        .Append(context.Parameters.AddParameter(V1.QuantityCode.Value, expression.Value))
                        .Append(")");

                    return context;

                case FieldName.QuantitySystem:
                    if (context.Model.TryGetSystemId(expression.Value, out var systemId))
                    {
                        return VisitSimpleBinary(BinaryOperator.Equal, context, V1.QuantitySearchParam.SystemId, expression.ComponentIndex, systemId);
                    }

                    context.StringBuilder.Append(V1.QuantitySearchParam.SystemId, context.TableAlias)
                        .Append(" IN (SELECT ")
                        .Append(V1.System.SystemId, null)
                        .Append(" FROM ").Append(V1.System)
                        .Append(" WHERE ")
                        .Append(V1.System.Value, null)
                        .Append(" = ")
                        .Append(context.Parameters.AddParameter(V1.System.Value, expression.Value))
                        .Append(")");

                    return context;

                default:
                    throw new ArgumentOutOfRangeException(expression.FieldName.ToString());
            }
        }
    }
}
